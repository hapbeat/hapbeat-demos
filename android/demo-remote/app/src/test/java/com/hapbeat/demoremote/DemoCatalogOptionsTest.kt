package com.hapbeat.demoremote

import org.junit.Assert.assertEquals
import org.junit.Test

class DemoCatalogOptionsTest {
    private fun option(demoId: String, id: String) = DemoCatalog.optionsFor(demoId).first { it.id == id }

    @Test
    fun missingKeyShowsTheDescriptorDefault() {
        val points = option(DemoCatalog.VOLLEY_ID, "points")
        assertEquals("7", DemoCatalog.chosenValue(points, emptyMap()))
        assertEquals("3", DemoCatalog.chosenValue(points, mapOf("points" to "3")))
        // An existing preset step without the key opens with the default chosen.
        val tutorial = option(DemoCatalog.ENERGY_DUEL_ID, "tutorial")
        assertEquals("on", DemoCatalog.chosenValue(tutorial, mapOf("mode" to "free")))
    }

    @Test
    fun choosingTheDefaultLeavesTheKeyOut() {
        val points = option(DemoCatalog.VOLLEY_ID, "points")
        assertEquals(mapOf("points" to "3"), DemoCatalog.withChoice(emptyMap(), points, "3"))
        assertEquals(emptyMap<String, String>(), DemoCatalog.withChoice(mapOf("points" to "3"), points, "7"))
        assertEquals(emptyMap<String, String>(), DemoCatalog.withChoice(emptyMap(), points, "7"))
        // Nothing to summarize while everything is the default.
        assertEquals("", DemoCatalog.optionSummary(DemoCatalog.VOLLEY_ID, DemoCatalog.withChoice(emptyMap(), points, "7")))
    }

    @Test
    fun whenConditionsFollowTheChosenScene() {
        val scene = option(DemoCatalog.VOLLEY_ID, "scene")
        val receive = DemoCatalog.applicableOptions(DemoCatalog.VOLLEY_ID, DemoCatalog.withChoice(emptyMap(), scene, "receive"))
        assertEquals(listOf("scene", "balls"), DemoCatalog.activeOptionsFor(DemoCatalog.VOLLEY_ID, receive).map { it.id })
        val withBalls = DemoCatalog.applicableOptions(DemoCatalog.VOLLEY_ID, receive + ("balls" to "20"))
        assertEquals(mapOf("scene" to "receive", "balls" to "20"), withBalls)
        // Back to the default scene (block): the key goes, balls no longer applies and is dropped, points applies again.
        val back = DemoCatalog.applicableOptions(DemoCatalog.VOLLEY_ID, DemoCatalog.withChoice(withBalls, scene, "block"))
        assertEquals(emptyMap<String, String>(), back)
        assertEquals(listOf("scene", "points"), DemoCatalog.activeOptionsFor(DemoCatalog.VOLLEY_ID, back).map { it.id })
    }
}
