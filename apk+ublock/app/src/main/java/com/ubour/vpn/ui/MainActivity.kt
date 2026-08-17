package com.ubour.vpn.ui

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.SharedPreferences
import android.content.res.ColorStateList
import android.graphics.Color
import android.graphics.drawable.Drawable
import android.net.Uri
import android.net.VpnService
import android.os.Build
import android.os.Bundle
import android.os.PowerManager
import android.provider.Settings
import android.text.Editable
import android.text.TextWatcher
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.AdapterView
import android.widget.ArrayAdapter
import android.widget.EditText
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.appcompat.app.AppCompatDelegate
import androidx.core.content.ContextCompat
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.repeatOnLifecycle
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView
import com.ubour.vpn.R
import com.ubour.vpn.adblock.AdBlockEngine
import com.ubour.vpn.adblock.FilterUpdateService
import com.ubour.vpn.core.AppOperationMode
import com.ubour.vpn.core.TrafficStats
import com.ubour.vpn.core.VpnState
import com.ubour.vpn.core.VpnStateManager
import com.ubour.vpn.databinding.ActivityMainBinding
import com.ubour.vpn.databinding.DialogAppsSelectorBinding
import com.ubour.vpn.databinding.DialogSettingsBinding
import com.ubour.vpn.databinding.DialogUpdateBinding
import com.ubour.vpn.service.UbourVpnService
import com.ubour.vpn.service.UpdateInfo
import com.ubour.vpn.service.UpdateService
import kotlinx.coroutines.launch
import java.util.Locale

class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding
    private lateinit var prefs: SharedPreferences

    private val vpnPrepareLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (result.resultCode == RESULT_OK) {
            startVpnService()
        } else {
            Toast.makeText(this, R.string.vpn_permission_required, Toast.LENGTH_SHORT).show()
        }
    }

    private val notificationPermissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { isGranted ->
        if (!isGranted) {
            Toast.makeText(this, R.string.notification_permission_required, Toast.LENGTH_SHORT).show()
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        // Apply saved theme before view creation
        prefs = getSharedPreferences("ubour_settings", Context.MODE_PRIVATE)
        val savedTheme = prefs.getInt("app_theme", AppCompatDelegate.MODE_NIGHT_FOLLOW_SYSTEM)
        AppCompatDelegate.setDefaultNightMode(savedTheme)

        super.onCreate(savedInstanceState)
        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        // Initialize AdBlock rules in background & check online updates
        lifecycleScope.launch {
            AdBlockEngine.initialize(applicationContext)
            if (FilterUpdateService.isUpdateNeeded(applicationContext)) {
                FilterUpdateService.updateFiltersOnline(applicationContext)
            }
            
            // Auto check app update in background
            val update = UpdateService.checkForAppUpdate(applicationContext)
            if (update.hasUpdate) {
                binding.btnCheckUpdates.setColorFilter(ContextCompat.getColor(this@MainActivity, R.color.accent_amber))
            }
        }

        checkPermissions()
        setupUI()
        observeVpnState()
    }

    private fun checkPermissions() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            notificationPermissionLauncher.launch(Manifest.permission.POST_NOTIFICATIONS)
        }
    }

    private fun setupUI() {
        // Restore saved settings
        val isAdBlockEnabled = prefs.getBoolean("adblock_enabled", true)
        binding.switchAdBlock.isChecked = isAdBlockEnabled

        val savedOpMode = prefs.getInt("op_mode", AppOperationMode.WARP_AND_ADBLOCK.ordinal)
        when (savedOpMode) {
            AppOperationMode.VPN_AND_ADBLOCK.ordinal -> {
                binding.rbOpFull.isChecked = true
                binding.bypassModeCard.visibility = View.VISIBLE
            }
            AppOperationMode.ADBLOCK_ONLY.ordinal -> {
                binding.rbOpAdBlockOnly.isChecked = true
                binding.bypassModeCard.visibility = View.GONE
            }
            AppOperationMode.VPN_ONLY.ordinal -> {
                binding.rbOpVpnOnly.isChecked = true
                binding.bypassModeCard.visibility = View.VISIBLE
            }
            else -> {
                binding.rbOpWarp.isChecked = true
                binding.bypassModeCard.visibility = View.GONE
            }
        }

        binding.switchAdBlock.setOnCheckedChangeListener { _, isChecked ->
            prefs.edit().putBoolean("adblock_enabled", isChecked).apply()
            if (!isChecked) {
                binding.rbOpVpnOnly.isChecked = true
            } else if (binding.rbOpVpnOnly.isChecked) {
                binding.rbOpWarp.isChecked = true
            }
        }

        binding.rgOpModes.setOnCheckedChangeListener { _, checkedId ->
            val opMode = when (checkedId) {
                R.id.rbOpFull -> {
                    binding.bypassModeCard.visibility = View.VISIBLE
                    AppOperationMode.VPN_AND_ADBLOCK
                }
                R.id.rbOpAdBlockOnly -> {
                    binding.bypassModeCard.visibility = View.GONE
                    AppOperationMode.ADBLOCK_ONLY
                }
                R.id.rbOpVpnOnly -> {
                    binding.bypassModeCard.visibility = View.VISIBLE
                    AppOperationMode.VPN_ONLY
                }
                else -> {
                    binding.bypassModeCard.visibility = View.GONE
                    AppOperationMode.WARP_AND_ADBLOCK
                }
            }
            prefs.edit().putInt("op_mode", opMode.ordinal).apply()
        }

        binding.btnPower.setOnClickListener {
            when (VpnStateManager.state.value) {
                VpnState.DISCONNECTED -> prepareAndStartVpn()
                VpnState.CONNECTED -> stopVpnService()
                VpnState.CONNECTING, VpnState.DISCONNECTING -> { /* Ignore */ }
            }
        }

        binding.btnSettings.setOnClickListener {
            showSettingsDialog()
        }

        binding.btnCheckUpdates.setOnClickListener {
            showUpdateDialog()
        }
    }

    private fun prepareAndStartVpn() {
        val vpnIntent = VpnService.prepare(this)
        if (vpnIntent != null) {
            vpnPrepareLauncher.launch(vpnIntent)
        } else {
            startVpnService()
        }
    }

    private fun getSelectedBypassMode(): Int {
        return when {
            binding.rbModeFast.isChecked -> 2
            binding.rbModeAggressive.isChecked -> 3
            else -> 1
        }
    }

    private fun getSelectedOpMode(): AppOperationMode {
        return when {
            binding.rbOpFull.isChecked -> AppOperationMode.VPN_AND_ADBLOCK
            binding.rbOpAdBlockOnly.isChecked -> AppOperationMode.ADBLOCK_ONLY
            binding.rbOpVpnOnly.isChecked -> AppOperationMode.VPN_ONLY
            else -> AppOperationMode.WARP_AND_ADBLOCK
        }
    }

    private fun startVpnService() {
        val bypassMode = getSelectedBypassMode()
        val opMode = getSelectedOpMode()
        val isAdBlockEnabled = binding.switchAdBlock.isChecked
        val dns = prefs.getString("selected_dns", "94.140.14.14") ?: "94.140.14.14"

        val serviceIntent = Intent(this, UbourVpnService::class.java).apply {
            action = UbourVpnService.ACTION_START
            putExtra(UbourVpnService.EXTRA_MODE, bypassMode)
            putExtra(UbourVpnService.EXTRA_DNS, dns)
            putExtra(UbourVpnService.EXTRA_OP_MODE, opMode.ordinal)
            putExtra(UbourVpnService.EXTRA_ADBLOCK_ENABLED, isAdBlockEnabled)
        }
        ContextCompat.startForegroundService(this, serviceIntent)
    }

    private fun stopVpnService() {
        val serviceIntent = Intent(this, UbourVpnService::class.java).apply {
            action = UbourVpnService.ACTION_STOP
        }
        startService(serviceIntent)
    }

    private fun observeVpnState() {
        lifecycleScope.launch {
            repeatOnLifecycle(Lifecycle.State.STARTED) {
                launch {
                    VpnStateManager.state.collect { state ->
                        updateUIForState(state)
                    }
                }
                launch {
                    VpnStateManager.stats.collect { stats ->
                        updateStatsUI(stats)
                    }
                }
            }
        }
    }

    private fun updateUIForState(state: VpnState) {
        val emerald = ContextCompat.getColor(this, R.color.accent_emerald)
        val crimson = ContextCompat.getColor(this, R.color.status_disconnected)
        val muted = ContextCompat.getColor(this, R.color.power_btn_off_ring)
        val amber = ContextCompat.getColor(this, R.color.accent_amber)

        when (state) {
            VpnState.DISCONNECTED -> {
                binding.tvStatusTitle.text = getString(R.string.status_disconnected)
                binding.tvStatusDesc.text = getString(R.string.status_desc_off)
                binding.statusDot.backgroundTintList = ColorStateList.valueOf(crimson)
                binding.powerGlow.visibility = View.INVISIBLE
                binding.btnPower.backgroundTintList = ColorStateList.valueOf(ContextCompat.getColor(this, R.color.power_btn_off_bg))
                binding.btnPower.setColorFilter(muted)
                binding.statsCard.visibility = View.GONE
                setModeControlsEnabled(true)
            }
            VpnState.CONNECTING -> {
                binding.tvStatusTitle.text = getString(R.string.status_connecting)
                binding.tvStatusDesc.text = getString(R.string.status_desc_off)
                binding.statusDot.backgroundTintList = ColorStateList.valueOf(amber)
                binding.powerGlow.visibility = View.VISIBLE
                binding.powerGlow.backgroundTintList = ColorStateList.valueOf(amber)
                binding.btnPower.setColorFilter(amber)
                binding.statsCard.visibility = View.GONE
                setModeControlsEnabled(false)
            }
            VpnState.CONNECTED -> {
                val opMode = VpnStateManager.currentMode.value
                val statusText = when (opMode) {
                    AppOperationMode.WARP_AND_ADBLOCK -> getString(R.string.status_connected_warp)
                    AppOperationMode.VPN_AND_ADBLOCK -> getString(R.string.status_connected_full)
                    AppOperationMode.ADBLOCK_ONLY -> getString(R.string.status_connected_adblock_only)
                    AppOperationMode.VPN_ONLY -> getString(R.string.status_connected)
                    AppOperationMode.CUSTOM_VLESS -> getString(R.string.status_connected_vless)
                }
                binding.tvStatusTitle.text = statusText
                binding.tvStatusDesc.text = getString(R.string.status_desc_on)
                binding.statusDot.backgroundTintList = ColorStateList.valueOf(emerald)
                binding.powerGlow.visibility = View.VISIBLE
                binding.powerGlow.backgroundTintList = ColorStateList.valueOf(crimson)
                binding.btnPower.backgroundTintList = ColorStateList.valueOf(ContextCompat.getColor(this, R.color.power_btn_on_bg))
                binding.btnPower.setColorFilter(emerald)
                binding.statsCard.visibility = View.VISIBLE
                setModeControlsEnabled(false)
            }
            VpnState.DISCONNECTING -> {
                binding.tvStatusTitle.text = getString(R.string.status_disconnecting)
                binding.statusDot.backgroundTintList = ColorStateList.valueOf(amber)
                binding.powerGlow.visibility = View.INVISIBLE
                binding.btnPower.setColorFilter(amber)
                setModeControlsEnabled(false)
            }
        }
    }

    private fun updateStatsUI(stats: TrafficStats) {
        val elapsedSec = if (stats.connectedSince > 0) (System.currentTimeMillis() - stats.connectedSince) / 1000 else 0
        val hours = elapsedSec / 3600
        val mins = (elapsedSec % 3600) / 60
        val secs = elapsedSec % 60
        binding.tvDuration.text = String.format(Locale.US, "%02d:%02d:%02d", hours, mins, secs)

        binding.tvDownload.text = formatBytes(stats.rxBytes)
        binding.tvBlockedAds.text = stats.blockedAds.toString()
        binding.tvBlockedTrackers.text = stats.blockedTrackers.toString()
    }

    private fun formatBytes(bytes: Long): String {
        return when {
            bytes >= 1024 * 1024 * 1024 -> String.format(Locale.US, "%.1f GB", bytes / (1024.0 * 1024.0 * 1024.0))
            bytes >= 1024 * 1024 -> String.format(Locale.US, "%.1f MB", bytes / (1024.0 * 1024.0))
            bytes >= 1024 -> String.format(Locale.US, "%.1f KB", bytes / 1024.0)
            else -> "$bytes B"
        }
    }

    private fun setModeControlsEnabled(enabled: Boolean) {
        binding.switchAdBlock.isEnabled = enabled
        binding.rbOpWarp.isEnabled = enabled
        binding.rbOpFull.isEnabled = enabled
        binding.rbOpAdBlockOnly.isEnabled = enabled
        binding.rbOpVpnOnly.isEnabled = enabled
        binding.rbModeStandard.isEnabled = enabled
        binding.rbModeFast.isEnabled = enabled
        binding.rbModeAggressive.isEnabled = enabled
    }

    private fun showSettingsDialog() {
        val dialogBinding = DialogSettingsBinding.inflate(layoutInflater)
        val dialog = AlertDialog.Builder(this)
            .setView(dialogBinding.root)
            .create()

        dialog.window?.setBackgroundDrawableResource(android.R.color.transparent)

        // Theme Options
        val themeOptions = listOf(
            getString(R.string.theme_system),
            getString(R.string.theme_dark),
            getString(R.string.theme_light)
        )
        val themeModes = listOf(
            AppCompatDelegate.MODE_NIGHT_FOLLOW_SYSTEM,
            AppCompatDelegate.MODE_NIGHT_YES,
            AppCompatDelegate.MODE_NIGHT_NO
        )

        val themeAdapter = ArrayAdapter(this, android.R.layout.simple_spinner_dropdown_item, themeOptions)
        dialogBinding.spTheme.adapter = themeAdapter

        val currentTheme = prefs.getInt("app_theme", AppCompatDelegate.MODE_NIGHT_FOLLOW_SYSTEM)
        val currentThemeIdx = themeModes.indexOf(currentTheme).coerceAtLeast(0)
        dialogBinding.spTheme.setSelection(currentThemeIdx)

        dialogBinding.spTheme.onItemSelectedListener = object : AdapterView.OnItemSelectedListener {
            override fun onItemSelected(parent: AdapterView<*>?, view: View?, position: Int, id: Long) {
                val selectedTheme = themeModes[position]
                if (selectedTheme != currentTheme) {
                    prefs.edit().putInt("app_theme", selectedTheme).apply()
                    AppCompatDelegate.setDefaultNightMode(selectedTheme)
                }
            }
            override fun onNothingSelected(parent: AdapterView<*>?) {}
        }

        // DNS Options
        val dnsOptions = listOf(
            "AdGuard DNS (94.140.14.14) - مستحسن لمنع الإعلانات",
            "Cloudflare (1.1.1.1)",
            "Google (8.8.8.8)",
            "Quad9 (9.9.9.9)"
        )
        val dnsIps = listOf("94.140.14.14", "1.1.1.1", "8.8.8.8", "9.9.9.9")

        val adapter = ArrayAdapter(this, android.R.layout.simple_spinner_dropdown_item, dnsOptions)
        dialogBinding.spDns.adapter = adapter

        val currentDns = prefs.getString("selected_dns", "94.140.14.14")
        val selectedIdx = dnsIps.indexOf(currentDns).coerceAtLeast(0)
        dialogBinding.spDns.setSelection(selectedIdx)

        dialogBinding.tvFilterStatus.text = getString(R.string.filter_update_success, AdBlockEngine.totalRules)

        dialogBinding.btnUpdateFilters.setOnClickListener {
            dialogBinding.pbFilterUpdate.visibility = View.VISIBLE
            dialogBinding.btnUpdateFilters.isEnabled = false
            lifecycleScope.launch {
                val result = FilterUpdateService.updateFiltersOnline(applicationContext)
                dialogBinding.pbFilterUpdate.visibility = View.GONE
                dialogBinding.btnUpdateFilters.isEnabled = true
                if (result.success) {
                    dialogBinding.tvFilterStatus.text = getString(R.string.filter_update_success, result.rulesCount)
                    Toast.makeText(this@MainActivity, result.message, Toast.LENGTH_SHORT).show()
                } else {
                    dialogBinding.tvFilterStatus.text = getString(R.string.filter_update_failed)
                    Toast.makeText(this@MainActivity, result.message, Toast.LENGTH_SHORT).show()
                }
            }
        }

        // Split Tunneling
        fun updateExcludedAppsLabel() {
            val excludedCount = (prefs.getStringSet("excluded_apps", emptySet()) ?: emptySet()).size
            dialogBinding.tvSplitTunnelStatus.text = if (excludedCount > 0) {
                getString(R.string.apps_excluded_count, excludedCount)
            } else {
                getString(R.string.settings_split_tunnel_desc)
            }
        }
        updateExcludedAppsLabel()

        dialogBinding.btnSelectExcludedApps.setOnClickListener {
            showExcludedAppsDialog {
                updateExcludedAppsLabel()
            }
        }

        fun updateBatteryUI() {
            val pm = getSystemService(Context.POWER_SERVICE) as? PowerManager
            val isExempted = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                pm?.isIgnoringBatteryOptimizations(packageName) == true
            } else {
                true
            }

            val emerald = ContextCompat.getColor(this@MainActivity, R.color.accent_emerald)
            if (isExempted) {
                dialogBinding.btnBatteryOptimization.text = getString(R.string.battery_already_optimized)
                dialogBinding.btnBatteryOptimization.backgroundTintList = ColorStateList.valueOf(emerald)
                dialogBinding.btnBatteryOptimization.setTextColor(Color.WHITE)
            } else {
                dialogBinding.btnBatteryOptimization.text = getString(R.string.btn_fix_battery)
                dialogBinding.btnBatteryOptimization.backgroundTintList = ColorStateList.valueOf(ContextCompat.getColor(this@MainActivity, R.color.surface_soft))
                dialogBinding.btnBatteryOptimization.setTextColor(ContextCompat.getColor(this@MainActivity, R.color.text_primary))
            }
        }
        updateBatteryUI()

        dialogBinding.btnBatteryOptimization.setOnClickListener {
            val pm = getSystemService(Context.POWER_SERVICE) as? PowerManager
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M && pm?.isIgnoringBatteryOptimizations(packageName) == true) {
                Toast.makeText(this, "التطبيق مستثنى حالياً من قيود توفير البطارية", Toast.LENGTH_SHORT).show()
            } else {
                requestIgnoreBatteryOptimization()
            }
        }

        dialogBinding.btnCloseSettings.setOnClickListener {
            val selectedPosition = dialogBinding.spDns.selectedItemPosition
            if (selectedPosition in dnsIps.indices) {
                prefs.edit().putString("selected_dns", dnsIps[selectedPosition]).apply()
            }
            dialog.dismiss()
        }

        dialog.show()
    }

    private fun showExcludedAppsDialog(onAppsUpdated: () -> Unit) {
        val dialogBinding = DialogAppsSelectorBinding.inflate(layoutInflater)
        val dialog = AlertDialog.Builder(this)
            .setView(dialogBinding.root)
            .create()

        dialog.window?.setBackgroundDrawableResource(android.R.color.transparent)

        val pm = packageManager
        val savedExcluded = prefs.getStringSet("excluded_apps", emptySet()) ?: emptySet()

        val installedApps = pm.getInstalledApplications(0)
            .filter { app ->
                app.packageName != packageName && (
                    (app.flags and android.content.pm.ApplicationInfo.FLAG_SYSTEM == 0) ||
                    (app.flags and android.content.pm.ApplicationInfo.FLAG_UPDATED_SYSTEM_APP != 0) ||
                    pm.getLaunchIntentForPackage(app.packageName) != null
                )
            }
            .distinctBy { it.packageName }
            .sortedBy { it.loadLabel(pm).toString().lowercase() }
            .map { app ->
                AppItem(
                    name = app.loadLabel(pm).toString(),
                    packageName = app.packageName,
                    icon = app.loadIcon(pm),
                    isExcluded = savedExcluded.contains(app.packageName)
                )
            }

        var currentMode = AppFilterMode.ALL

        fun updateUI(excludedCount: Int, totalCount: Int) {
            dialogBinding.tvSelectedCount.text = getString(R.string.selected_apps_count, excludedCount)
            dialogBinding.btnTabAll.text = getString(R.string.tab_all_apps, totalCount)
            dialogBinding.btnTabExcluded.text = getString(R.string.tab_excluded_apps, excludedCount)

            if (currentMode == AppFilterMode.EXCLUDED_ONLY && excludedCount == 0) {
                dialogBinding.tvEmptyList.visibility = View.VISIBLE
                dialogBinding.rvApps.visibility = View.GONE
            } else {
                dialogBinding.tvEmptyList.visibility = View.GONE
                dialogBinding.rvApps.visibility = View.VISIBLE
            }
        }

        val adapter = AppsAdapter(installedApps) { excludedCount, totalCount ->
            updateUI(excludedCount, totalCount)
        }

        dialogBinding.rvApps.layoutManager = LinearLayoutManager(this)
        dialogBinding.rvApps.adapter = adapter
        updateUI(adapter.getSelectedCount(), adapter.getTotalCount())

        val textMuted = ContextCompat.getColor(this, R.color.text_muted)

        fun setTab(mode: AppFilterMode) {
            currentMode = mode
            adapter.setFilterMode(mode)
            if (mode == AppFilterMode.ALL) {
                dialogBinding.btnTabAll.setBackgroundResource(R.drawable.bg_tab_selected)
                dialogBinding.btnTabAll.setTextColor(Color.WHITE)
                dialogBinding.btnTabExcluded.setBackgroundResource(0)
                dialogBinding.btnTabExcluded.setTextColor(textMuted)
            } else {
                dialogBinding.btnTabExcluded.setBackgroundResource(R.drawable.bg_tab_selected)
                dialogBinding.btnTabExcluded.setTextColor(Color.WHITE)
                dialogBinding.btnTabAll.setBackgroundResource(0)
                dialogBinding.btnTabAll.setTextColor(textMuted)
            }
            updateUI(adapter.getSelectedCount(), adapter.getTotalCount())
        }

        dialogBinding.btnTabAll.setOnClickListener { setTab(AppFilterMode.ALL) }
        dialogBinding.btnTabExcluded.setOnClickListener { setTab(AppFilterMode.EXCLUDED_ONLY) }

        dialogBinding.etSearchApps.addTextChangedListener(object : TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
            override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {
                adapter.filter(s.toString())
                if (adapter.itemCount == 0) {
                    dialogBinding.tvEmptyList.visibility = View.VISIBLE
                    dialogBinding.rvApps.visibility = View.GONE
                } else {
                    dialogBinding.tvEmptyList.visibility = View.GONE
                    dialogBinding.rvApps.visibility = View.VISIBLE
                }
            }
            override fun afterTextChanged(s: Editable?) {}
        })

        dialogBinding.btnCancelApps.setOnClickListener { dialog.dismiss() }
        dialogBinding.btnSaveApps.setOnClickListener {
            val selectedPackages = adapter.getSelectedPackages()
            prefs.edit().putStringSet("excluded_apps", selectedPackages).apply()
            onAppsUpdated()
            dialog.dismiss()
            Toast.makeText(this, getString(R.string.apps_excluded_count, selectedPackages.size), Toast.LENGTH_SHORT).show()
        }

        dialog.show()
    }

    private fun requestIgnoreBatteryOptimization() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            val pm = getSystemService(Context.POWER_SERVICE) as PowerManager
            if (!pm.isIgnoringBatteryOptimizations(packageName)) {
                val intent = Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS).apply {
                    data = Uri.parse("package:$packageName")
                }
                startActivity(intent)
            } else {
                Toast.makeText(this, "تم استثناء التطبيق مسبقاً من تحسين البطارية", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun showUpdateDialog() {
        val dialogBinding = DialogUpdateBinding.inflate(layoutInflater)
        val dialog = AlertDialog.Builder(this)
            .setView(dialogBinding.root)
            .create()

        dialog.window?.setBackgroundDrawableResource(android.R.color.transparent)

        dialogBinding.btnCloseUpdate.setOnClickListener {
            dialog.dismiss()
        }

        dialog.show()

        lifecycleScope.launch {
            val result = UpdateService.checkForAppUpdate(applicationContext)
            dialogBinding.pbUpdate.visibility = View.GONE

            if (result.hasUpdate) {
                dialogBinding.tvUpdateTitle.text = getString(R.string.update_available_title)
                dialogBinding.tvUpdateMsg.text = getString(R.string.update_available_msg, result.latestVersion ?: "")
                dialogBinding.btnDownload.visibility = View.VISIBLE
                dialogBinding.btnDownload.setOnClickListener {
                    val url = result.downloadUrl ?: result.releasePageUrl
                    url?.let {
                        val browserIntent = Intent(Intent.ACTION_VIEW, Uri.parse(it))
                        startActivity(browserIntent)
                    }
                    dialog.dismiss()
                }
            } else {
                dialogBinding.tvUpdateMsg.text = getString(R.string.update_current_msg, "1.0.0")
            }
        }
    }
}
