package com.ubour.vpn.ui

import android.graphics.drawable.Drawable
import android.view.LayoutInflater
import android.view.ViewGroup
import androidx.recyclerview.widget.RecyclerView
import com.ubour.vpn.databinding.ItemAppSelectionBinding

data class AppItem(
    val name: String,
    val packageName: String,
    val icon: Drawable,
    var isExcluded: Boolean
)

enum class AppFilterMode {
    ALL,
    EXCLUDED_ONLY
}

class AppsAdapter(
    private val allApps: List<AppItem>,
    private val onItemSelectionChanged: (Int, Int) -> Unit
) : RecyclerView.Adapter<AppsAdapter.AppViewHolder>() {

    private var filteredApps: MutableList<AppItem> = allApps.toMutableList()
    private var currentMode: AppFilterMode = AppFilterMode.ALL
    private var currentQuery: String = ""

    inner class AppViewHolder(val binding: ItemAppSelectionBinding) : RecyclerView.ViewHolder(binding.root) {
        fun bind(item: AppItem) {
            binding.ivAppIcon.setImageDrawable(item.icon)
            binding.tvAppName.text = item.name
            binding.tvPackageName.text = item.packageName
            binding.cbExcluded.isChecked = item.isExcluded

            fun toggle() {
                item.isExcluded = !item.isExcluded
                binding.cbExcluded.isChecked = item.isExcluded
                val pos = adapterPosition
                if (currentMode == AppFilterMode.EXCLUDED_ONLY && !item.isExcluded && pos != RecyclerView.NO_POSITION) {
                    filteredApps.removeAt(pos)
                    notifyItemRemoved(pos)
                }
                onItemSelectionChanged(getSelectedCount(), allApps.size)
            }

            binding.itemContainer.setOnClickListener { toggle() }
            binding.cbExcluded.setOnClickListener { toggle() }
        }
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): AppViewHolder {
        val binding = ItemAppSelectionBinding.inflate(LayoutInflater.from(parent.context), parent, false)
        return AppViewHolder(binding)
    }

    override fun onBindViewHolder(holder: AppViewHolder, position: Int) {
        holder.bind(filteredApps[position])
    }

    override fun getItemCount(): Int = filteredApps.size

    fun getSelectedPackages(): Set<String> {
        return allApps.filter { it.isExcluded }.map { it.packageName }.toSet()
    }

    fun getSelectedCount(): Int {
        return allApps.count { it.isExcluded }
    }

    fun getTotalCount(): Int {
        return allApps.size
    }

    fun setFilterMode(mode: AppFilterMode) {
        currentMode = mode
        applyFilter()
    }

    fun filter(query: String) {
        currentQuery = query.trim().lowercase()
        applyFilter()
    }

    private fun applyFilter() {
        val list = allApps.filter { app ->
            val matchesMode = when (currentMode) {
                AppFilterMode.ALL -> true
                AppFilterMode.EXCLUDED_ONLY -> app.isExcluded
            }
            val matchesQuery = if (currentQuery.isEmpty()) {
                true
            } else {
                app.name.lowercase().contains(currentQuery) || app.packageName.lowercase().contains(currentQuery)
            }
            matchesMode && matchesQuery
        }
        filteredApps = list.toMutableList()
        notifyDataSetChanged()
    }
}
