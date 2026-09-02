package com.streamvue.player.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Instant

class MediaLibraryBrowsePolicyTest {
    private val now = Instant.parse("2026-09-01T12:00:00Z")

    @Test
    fun `resume and recent boundaries match the Windows policy`() {
        val resumable = channel(
            number = 1,
            durationMs = 3_600_000,
            resumePositionMs = 600_000,
            addedAt = "2026-08-20T12:00:00Z"
        )
        val almostFinished = channel(
            number = 2,
            durationMs = 3_600_000,
            resumePositionMs = 3_580_000,
            addedAt = "2026-07-01T12:00:00Z"
        )

        assertTrue(resumable.canResume)
        assertFalse(almostFinished.canResume)
        assertTrue(MediaLibraryBrowsePolicy.matches(
            resumable,
            MediaLibraryBrowseMode.RecentlyAdded,
            now
        ))
        assertFalse(MediaLibraryBrowsePolicy.matches(
            almostFinished,
            MediaLibraryBrowseMode.RecentlyAdded,
            now
        ))
        assertFalse(resumable.copy(isMediaCenterItem = false).canResume)
    }

    @Test
    fun `editorial shelves summarize and order provider activity`() {
        val older = channel(
            number = 1,
            kind = ChannelKind.Movie,
            durationMs = 3_600_000,
            resumePositionMs = 500_000,
            addedAt = "2026-08-30T12:00:00Z",
            lastPlayedAt = "2026-08-25T12:00:00Z"
        )
        val newer = channel(
            number = 2,
            kind = ChannelKind.Series,
            durationMs = 2_400_000,
            resumePositionMs = 700_000,
            addedAt = "2026-08-15T12:00:00Z",
            lastPlayedAt = "2026-08-31T12:00:00Z"
        )

        assertEquals(
            MediaLibraryBrowseSummary(true, 2, 2, 1, 1),
            MediaLibraryBrowsePolicy.summarize(listOf(older, newer), now)
        )
        assertEquals(
            listOf(2, 1),
            MediaLibraryBrowsePolicy.order(
                listOf(older, newer),
                MediaLibraryBrowseMode.ContinueWatching
            ).map(Channel::number)
        )
        assertEquals(
            listOf(1, 2),
            MediaLibraryBrowsePolicy.order(
                listOf(older, newer),
                MediaLibraryBrowseMode.RecentlyAdded
            ).map(Channel::number)
        )
    }

    private fun channel(
        number: Int,
        kind: ChannelKind = ChannelKind.Movie,
        durationMs: Long,
        resumePositionMs: Long,
        addedAt: String,
        lastPlayedAt: String? = null
    ): Channel = Channel(
        id = number.toString(),
        number = number,
        name = "Title $number",
        streamUri = "streamvue-media://plex/server/item-$number",
        group = "Library",
        kind = kind,
        sourceId = "fixture",
        sourceName = "Plex",
        isMediaCenterItem = true,
        libraryId = "library",
        libraryTitle = "Library",
        durationMs = durationMs,
        resumePositionMs = resumePositionMs,
        addedAt = addedAt,
        lastPlayedAt = lastPlayedAt
    )
}
