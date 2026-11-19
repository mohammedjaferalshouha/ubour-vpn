package com.ubour.vpn.ui

import android.content.res.ColorStateList
import android.view.LayoutInflater
import android.view.ViewGroup
import androidx.core.content.ContextCompat
import androidx.recyclerview.widget.RecyclerView
import com.ubour.vpn.R
import com.ubour.vpn.databinding.ItemWarpEndpointBinding
import com.ubour.vpn.warp.EndpointCheckState
import com.ubour.vpn.warp.WarpEndpointStatus
import java.util.Locale

class WarpEndpointsAdapter(
    private val items: MutableList<WarpEndpointStatus>,
    private var selectedManualKey: String?,
    private val onSelectEndpoint: (WarpEndpointStatus) -> Unit,
    private val onMeasureSpeed: (WarpEndpointStatus, Int) -> Unit
) : RecyclerView.Adapter<WarpEndpointsAdapter.ViewHolder>() {

    var isFullScanRunning: Boolean = false
        set(value) {
            field = value
            notifyDataSetChanged()
        }

    inner class ViewHolder(val binding: ItemWarpEndpointBinding) :
        RecyclerView.ViewHolder(binding.root)

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): ViewHolder {
        val binding = ItemWarpEndpointBinding.inflate(
            LayoutInflater.from(parent.context),
            parent,
            false
        )
        return ViewHolder(binding)
    }

    override fun onBindViewHolder(holder: ViewHolder, position: Int) {
        val item = items[position]
        val ctx = holder.itemView.context

        holder.binding.tvEndpointAddress.text = "${item.host}:${item.port}"
        holder.binding.tvEndpointNote.text = item.noteArabic

        val isSelected = item.key == selectedManualKey
        holder.binding.itemEndpointRoot.setBackgroundResource(
            if (isSelected) R.drawable.bg_card_selected else R.drawable.bg_mode_pill
        )

        val emerald = ContextCompat.getColor(ctx, R.color.accent_emerald)
        val amber = ContextCompat.getColor(ctx, R.color.accent_amber)
        val crimson = ContextCompat.getColor(ctx, R.color.status_disconnected)
        val muted = ContextCompat.getColor(ctx, R.color.text_muted)

        when (item.checkState) {
            EndpointCheckState.UNTESTED -> {
                holder.binding.viewEndpointDot.backgroundTintList = ColorStateList.valueOf(muted)
                holder.binding.tvEndpointStatus.setTextColor(muted)
                holder.binding.tvEndpointStatus.text = ctx.getString(R.string.endpoint_state_untested)
            }
            EndpointCheckState.TESTING -> {
                holder.binding.viewEndpointDot.backgroundTintList = ColorStateList.valueOf(amber)
                holder.binding.tvEndpointStatus.setTextColor(amber)
                holder.binding.tvEndpointStatus.text = ctx.getString(R.string.endpoint_state_testing)
            }
            EndpointCheckState.WORKING -> {
                holder.binding.viewEndpointDot.backgroundTintList = ColorStateList.valueOf(emerald)
                holder.binding.tvEndpointStatus.setTextColor(emerald)
                val spd = item.speedMbps
                holder.binding.tvEndpointStatus.text = if (spd != null) {
                    ctx.getString(
                        R.string.endpoint_state_working_speed,
                        item.latencyMs,
                        String.format(Locale.US, "%.1f", spd)
                    )
                } else {
                    ctx.getString(R.string.endpoint_state_working, item.latencyMs)
                }
            }
            EndpointCheckState.FAILED -> {
                holder.binding.viewEndpointDot.backgroundTintList = ColorStateList.valueOf(crimson)
                holder.binding.tvEndpointStatus.setTextColor(crimson)
                holder.binding.tvEndpointStatus.text = ctx.getString(R.string.endpoint_state_failed)
            }
        }

        holder.binding.btnMeasureSpeed.text = if (item.isSpeedTesting) {
            ctx.getString(R.string.btn_measuring_speed)
        } else {
            ctx.getString(R.string.btn_measure_speed)
        }
        holder.binding.btnMeasureSpeed.isEnabled = !isFullScanRunning && !item.isSpeedTesting

        holder.binding.itemEndpointRoot.setOnClickListener {
            onSelectEndpoint(item)
        }
        holder.binding.btnMeasureSpeed.setOnClickListener {
            val adapterPos = holder.adapterPosition
            if (adapterPos != RecyclerView.NO_POSITION) {
                onMeasureSpeed(items[adapterPos], adapterPos)
            }
        }
    }

    override fun getItemCount(): Int = items.size

    fun setSelectedManualEndpoint(key: String?) {
        selectedManualKey = key
        notifyDataSetChanged()
    }

    fun sortWorkingFirst() {
        items.sortWith(
            compareBy<WarpEndpointStatus> {
                when (it.checkState) {
                    EndpointCheckState.WORKING -> 0
                    EndpointCheckState.TESTING -> 1
                    EndpointCheckState.UNTESTED -> 2
                    EndpointCheckState.FAILED -> 3
                }
            }.thenBy {
                if (it.checkState == EndpointCheckState.WORKING && it.latencyMs > 0) it.latencyMs else Long.MAX_VALUE
            }
        )
        notifyDataSetChanged()
    }
}
