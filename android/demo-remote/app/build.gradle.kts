import java.net.URI
import java.security.MessageDigest

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

android {
    namespace = "com.hapbeat.demoremote"
    compileSdk = 36

    defaultConfig {
        applicationId = "com.hapbeat.demoremote"
        minSdk = 26
        targetSdk = 35
        versionCode = 1
        versionName = "0.1.0-d1"
    }

    buildTypes {
        release {
            isMinifyEnabled = false
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
    buildFeatures {
        compose = true
        buildConfig = true
    }
    packaging {
        resources.excludes += setOf("META-INF/AL2.0", "META-INF/LGPL2.1")
    }
}

// scrcpy-server v4.1 (Apache-2.0). The binary is never committed: it is fetched at build
// time (or taken from the sibling tools/quest-mirror copy) and verified by SHA-256.
abstract class PrepareScrcpyServer : DefaultTask() {
    @get:Input abstract val url: Property<String>
    @get:Input abstract val sha256: Property<String>
    @get:Internal abstract val localCandidate: RegularFileProperty
    @get:Internal abstract val downloadCache: RegularFileProperty
    @get:OutputDirectory abstract val outputDir: DirectoryProperty

    @TaskAction
    fun prepare() {
        val expected = sha256.get()
        val local = localCandidate.get().asFile
        val cache = downloadCache.get().asFile
        val bytes = when {
            local.isFile && digest(local.readBytes()) == expected -> local.readBytes()
            cache.isFile && digest(cache.readBytes()) == expected -> cache.readBytes()
            else -> {
                logger.lifecycle("Downloading ${url.get()}")
                val downloaded = URI(url.get()).toURL().openStream().use { it.readBytes() }
                cache.parentFile.mkdirs()
                cache.writeBytes(downloaded)
                downloaded
            }
        }
        val actual = digest(bytes)
        if (actual != expected) throw GradleException("scrcpy-server SHA-256 mismatch: expected $expected, got $actual")
        val out = outputDir.get().asFile
        out.mkdirs()
        out.resolve("scrcpy-server.jar").writeBytes(bytes)
    }

    private fun digest(bytes: ByteArray): String =
        MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { "%02x".format(it) }
}

val prepareScrcpyServer = tasks.register<PrepareScrcpyServer>("prepareScrcpyServer") {
    url.set("https://github.com/Genymobile/scrcpy/releases/download/v4.1/scrcpy-server-v4.1")
    sha256.set("deacb991ed2509715160ffdc7907e47b4160eb30d1566217e9047fd5b8850cae")
    localCandidate.set(rootProject.layout.projectDirectory.file("../../tools/quest-mirror/scrcpy/scrcpy-server"))
    downloadCache.set(layout.buildDirectory.file("scrcpy-download/scrcpy-server-v4.1"))
    outputDir.set(layout.buildDirectory.dir("generated/scrcpyAssets"))
}

androidComponents {
    onVariants { variant ->
        variant.sources.assets?.addGeneratedSourceDirectory(prepareScrcpyServer, PrepareScrcpyServer::outputDir)
    }
}

dependencies {
    val composeBom = platform("androidx.compose:compose-bom:2025.05.01")
    implementation(composeBom)
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.foundation:foundation")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.core:core-ktx:1.16.0")
    implementation("androidx.activity:activity-compose:1.10.1")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.9.0")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.9.0")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.10.2")
    implementation("dev.mobile:dadb:2.0.0")

    testImplementation("junit:junit:4.13.2")
}
