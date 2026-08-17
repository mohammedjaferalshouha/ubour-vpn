package com.ubour.vpn.ui

import android.Manifest
import android.content.Context
import android.content.Intent
import android.content.SharedPreferences
import android.content.res.ColorStateList
import android.net.Uri
import android.net.VpnService
import android.os.Build
import android.os.Bundle
import android.os.PowerManager
import android.provider.Settings
import android.view.View
import android.widget.ArrayAdapter
import android.widget.Toast
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import com.ubour.vpn.R
import com.ubour.vpn.core.VpnState
import com.ubour.vpn.core.VpnStateManager
import com.ubour.vpn.databinding.ActivityMainBinding
import com.ubour.vpn.databinding.DialogSettingsBinding
import com.ubour.vpn.databinding.DialogUpdateBinding
import com.ubour.vpn.service.UbourVpnService
import com.ubour.vpn.service.UpdateService
import kotlinx.coroutines.flow.collectLatest
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
        super.onCreate(savedInstanceState)
        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        prefs = getSharedPreferences("ubour_settings", Context.MODE_PRIVATE)

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
        binding.btnPower.setOnClickListener {
            when (VpnStateManager.state.value) {
                VpnState.DISCONNECTED -> prepareAndStartVpn()
                VpnState.CONNECTED -> stopVpnService()
                VpnState.CONNECTING, VpnState.DISCONNECTING -> { /* Ignore while transitioning */ }
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

    private fun getSelectedMode(): Int {
        return when {
            binding.rbModeFast.isChecked -> 2
            binding.rbModeAggressive.isChecked -> 3
            else -> 1
        }
    }

    private fun startVpnService() {
        val mode = getSelectedMode()
        val dns = prefs.getString("selected_dns", "1.1.1.1") ?: "1.1.1.1"

        val serviceIntent = Intent(this, UbourVpnService::class.java).apply {
            action = UbourVpnService.ACTION_START
            putExtra(UbourVpnService.EXTRA_MODE, mode)
            putExtra(UbourVpnService.EXTRA_DNS, dns)
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            startForegroundService(serviceIntent)
        } else {
            startService(serviceIntent)
        }
    }

    private fun stopVpnService() {
        val serviceIntent = Intent(this, UbourVpnService::class.java).apply {
            action = UbourVpnService.ACTION_STOP
        }
        startService(serviceIntent)
    }

    private fun observeVpnState() {
        lifecycleScope.launch {
            VpnStateManager.state.collectLatest { state ->
                updateUiForState(state)
            }
        }

        lifecycleScope.launch {
            VpnStateManager.stats.collectLatest { stats ->
                if (stats.connectedSince > 0) {
                    val durationSec = (System.currentTimeMillis() - stats.connectedSince) / 1000
                    val hours = durationSec / 3600
                    val minutes = (durationSec % 3600) / 60
                    val seconds = durationSec % 60
                    binding.tvDuration.text = String.format(Locale.US, "%02d:%02d:%02d", hours, minutes, seconds)

                    val rxMb = stats.rxBytes / (1024.0 * 1024.0)
                    val txMb = stats.txBytes / (1024.0 * 1024.0)
                    binding.tvDownload.text = String.format(Locale.US, "%.1f MB", rxMb)
                    binding.tvUpload.text = String.format(Locale.US, "%.1f MB", txMb)
                }
            }
        }
    }

    private fun updateUiForState(state: VpnState) {
        when (state) {
            VpnState.DISCONNECTED -> {
                binding.tvStatusTitle.text = getString(R.string.status_disconnected)
                binding.tvStatusTitle.setTextColor(ContextCompat.getColor(this, R.color.text_primary))
                binding.tvStatusDesc.text = getString(R.string.status_desc_off)
                binding.statusDot.backgroundTintList = ColorStateList.valueOf(
                    ContextCompat.getColor(this, R.color.status_disconnected)
                )

                binding.btnPower.backgroundTintList = ColorStateList.valueOf(
                    ContextCompat.getColor(this, R.color.power_btn_off_bg)
                )
                binding.btnPower.imageTintList = ColorStateList.valueOf(
                    ContextCompat.getColor(this, R.color.text_muted)
                )
                binding.powerGlow.visibility = View.INVISIBLE
                binding.statsCard.visibility = View.GONE
                setModeSelectionEnabled(true)
            }
            VpnState.CONNECTING -> {
                binding.tvStatusTitle.text = getString(R.string.status_connecting)
                binding.tvStatusTitle.setTextColor(ContextCompat.getColor(this, R.color.status_connecting))
                binding.statusDot.backgroundTintList = ColorStateList.valueOf(
                    ContextCompat.getColor(this, R.color.status_connecting)
                )
                setModeSelectionEnabled(false)
            }
            VpnState.CONNECTED -> {
                binding.tvStatusTitle.text = getString(R.string.status_connected)
                binding.tvStatusTitle.setTextColor(ContextCompat.getColor(this, R.color.status_connected))
                binding.tvStatusDesc.text = getString(R.string.status_desc_on)
                binding.statusDot.backgroundTintList = ColorStateList.valueOf(
                    ContextCompat.getColor(this, R.color.status_connected)
                )

                binding.btnPower.backgroundTintList = ColorStateList.valueOf(
                    ContextCompat.getColor(this, R.color.power_btn_on_bg)
                )
                binding.btnPower.imageTintList = ColorStateList.valueOf(
                    ContextCompat.getColor(this, R.color.accent_emerald)
                )
                binding.powerGlow.visibility = View.VISIBLE
                binding.statsCard.visibility = View.VISIBLE
                setModeSelectionEnabled(false)
            }
            VpnState.DISCONNECTING -> {
                binding.tvStatusTitle.text = getString(R.string.status_disconnecting)
                binding.tvStatusTitle.setTextColor(ContextCompat.getColor(this, R.color.status_connecting))
                binding.powerGlow.visibility = View.INVISIBLE
            }
        }
    }

    private fun setModeSelectionEnabled(enabled: Boolean) {
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

        val dnsOptions = listOf("Cloudflare (1.1.1.1)", "Google (8.8.8.8)", "Quad9 (9.9.9.9)", "AdGuard DNS (94.140.14.14)")
        val dnsIps = listOf("1.1.1.1", "8.8.8.8", "9.9.9.9", "94.140.14.14")

        val adapter = ArrayAdapter(this, android.R.layout.simple_spinner_dropdown_item, dnsOptions)
        dialogBinding.spDns.adapter = adapter

        val currentDns = prefs.getString("selected_dns", "1.1.1.1")
        val selectedIdx = dnsIps.indexOf(currentDns).coerceAtLeast(0)
        dialogBinding.spDns.setSelection(selectedIdx)

        dialogBinding.btnBatteryOptimization.setOnClickListener {
            requestIgnoreBatteryOptimization()
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
            val result = UpdateService.checkEngineUpdate()
            dialogBinding.pbUpdate.visibility = View.GONE

            if (result.hasUpdate) {
                dialogBinding.tvUpdateTitle.text = getString(R.string.update_available_title)
                dialogBinding.tvUpdateMsg.text = getString(R.string.update_available_msg, result.latestVersion ?: "")
                dialogBinding.btnDownload.visibility = View.VISIBLE
                dialogBinding.btnDownload.setOnClickListener {
                    result.releaseUrl?.let { url ->
                        val browserIntent = Intent(Intent.ACTION_VIEW, Uri.parse(url))
                        startActivity(browserIntent)
                    }
                    dialog.dismiss()
                }
            } else {
                dialogBinding.tvUpdateMsg.text = getString(R.string.update_current_msg)
            }
        }
    }
}
