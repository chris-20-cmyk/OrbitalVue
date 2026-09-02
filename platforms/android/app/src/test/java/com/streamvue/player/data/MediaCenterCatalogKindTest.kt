package com.streamvue.player.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Test

class MediaCenterCatalogKindTest {
    @Test
    fun `every media-center item kind has a catalog presentation`() {
        // A kind without a presentation is dropped from the catalog, which is how a Plex or
        // Emby music library used to browse as empty on every platform.
        MediaCenterItemKind.entries.forEach { kind ->
            assertNotNull("$kind has no catalog channel kind", catalogKind(kind))
        }
    }

    @Test
    fun `audio items are presented as music, not replay`() {
        assertEquals(ChannelKind.Music, catalogKind(MediaCenterItemKind.Audio))
    }

    @Test
    fun `video and movie share a presentation and episodes are series`() {
        assertEquals(ChannelKind.Movie, catalogKind(MediaCenterItemKind.Movie))
        assertEquals(ChannelKind.Movie, catalogKind(MediaCenterItemKind.Video))
        assertEquals(ChannelKind.Series, catalogKind(MediaCenterItemKind.Episode))
        assertEquals(ChannelKind.Recording, catalogKind(MediaCenterItemKind.Recording))
        assertEquals(ChannelKind.Live, catalogKind(MediaCenterItemKind.LiveTv))
    }
}
