package com.hapbeat.demoremote

import org.junit.Assert.assertEquals
import org.junit.Test

class DemoCatalogOptionsTest {
    private fun option(demoId: String, id: String) = DemoCatalog.optionsFor(demoId).first { it.id == id }

    @Test
    fun missingKeyShowsTheDescriptorDefault() {
        val points = option(DemoCatalog.VOLLEY_ID, "points")
        assertEquals("4", DemoCatalog.chosenValue(points, emptyMap()))
        assertEquals("5", DemoCatalog.chosenValue(points, mapOf("points" to "5")))
        // An existing preset step without the key opens with the default chosen.
        val tutorial = option(DemoCatalog.ENERGY_DUEL_ID, "tutorial")
        assertEquals("on", DemoCatalog.chosenValue(tutorial, mapOf("mode" to "free")))
    }

    @Test
    fun choosingTheDefaultLeavesTheKeyOut() {
        val points = option(DemoCatalog.VOLLEY_ID, "points")
        assertEquals(mapOf("points" to "5"), DemoCatalog.withChoice(emptyMap(), points, "5"))
        assertEquals(emptyMap<String, String>(), DemoCatalog.withChoice(mapOf("points" to "5"), points, "4"))
        assertEquals(emptyMap<String, String>(), DemoCatalog.withChoice(emptyMap(), points, "4"))
        // Nothing to summarize while everything is the default.
        assertEquals("", DemoCatalog.optionSummary(DemoCatalog.VOLLEY_ID, DemoCatalog.withChoice(emptyMap(), points, "4")))
    }

    @Test
    fun whenConditionsFollowTheChosenScene() {
        val scene = option(DemoCatalog.VOLLEY_ID, "scene")
        val tutorial = option(DemoCatalog.VOLLEY_ID, "tutorial")
        val noTutorial = DemoCatalog.withChoice(emptyMap(), tutorial, "off")
        assertEquals(listOf("scene", "tutorial", "points"), DemoCatalog.activeOptionsFor(DemoCatalog.VOLLEY_ID, noTutorial).map { it.id })
        // The match has no tutorial: that choice no longer applies and is dropped.
        val match = DemoCatalog.applicableOptions(DemoCatalog.VOLLEY_ID, DemoCatalog.withChoice(noTutorial, scene, "match"))
        assertEquals(mapOf("scene" to "match"), match)
        assertEquals(listOf("scene", "points"), DemoCatalog.activeOptionsFor(DemoCatalog.VOLLEY_ID, match).map { it.id })
        // RECEIVE has the tutorial and the points like the spike + block turns.
        val receive = DemoCatalog.applicableOptions(DemoCatalog.VOLLEY_ID, DemoCatalog.withChoice(match, scene, "receive"))
        assertEquals(mapOf("scene" to "receive"), receive)
        assertEquals(listOf("scene", "tutorial", "points"), DemoCatalog.activeOptionsFor(DemoCatalog.VOLLEY_ID, receive).map { it.id })
    }
}
