package com.ubour.vpn.service

import org.junit.Assert.*
import org.junit.Test

class UpdateServiceTest {

    @Test
    fun testCurrentAppVersionNotEmpty() {
        assertNotNull(UpdateService.CURRENT_APP_VERSION)
        assertTrue(UpdateService.CURRENT_APP_VERSION.isNotBlank())
        assertTrue(UpdateService.CURRENT_APP_VERSION.matches(Regex("\\d+\\.\\d+\\.\\d+.*")))
    }

    @Test
    fun testUpdateInfoDataClass() {
        val updateInfo = UpdateInfo(
            hasUpdate = true,
            latestVersion = "1.5.0",
            downloadUrl = "https://example.com/app.apk",
            releaseNotes = "Fixes and improvements",
            releasePageUrl = "https://github.com/mohammedjaferalshouha/ubour-vpn/releases/tag/v1.5.0"
        )
        assertTrue(updateInfo.hasUpdate)
        assertEquals("1.5.0", updateInfo.latestVersion)
        assertEquals("https://example.com/app.apk", updateInfo.downloadUrl)
    }

    @Test
    fun testUpstreamComponentDataClass() {
        val component = UpstreamComponent(
            name = "Sing-box",
            repo = "SagerNet/sing-box",
            currentVersion = "1.13.19",
            latestVersion = "1.13.20",
            isUpToDate = false
        )
        assertEquals("Sing-box", component.name)
        assertEquals("SagerNet/sing-box", component.repo)
        assertFalse(component.isUpToDate)
    }

    @Test
    fun testFullSystemUpdateStatusDataClass() {
        val info = UpdateInfo(false, "1.1.0", null, null, null)
        val list = listOf(
            UpstreamComponent("ByeDPI", "hufrea/byedpi", "0.17.3", "0.17.3", true)
        )
        val status = FullSystemUpdateStatus(info, list)
        assertFalse(status.appUpdate.hasUpdate)
        assertEquals(1, status.upstreams.size)
        assertTrue(status.upstreams[0].isUpToDate)
    }
}
