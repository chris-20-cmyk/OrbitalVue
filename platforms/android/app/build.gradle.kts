import java.net.URI

plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
}

val streamVueDistributionMode = providers.gradleProperty("streamVueDistributionMode")
    .orElse("personal")
    .get()
    .trim()
    .lowercase()
require(streamVueDistributionMode == "personal" || streamVueDistributionMode == "store") {
    "streamVueDistributionMode must be personal or store."
}
val streamVuePremiumProductId = providers.gradleProperty("streamVuePremiumProductId")
    .orElse("")
    .get()
    .trim()
require(streamVuePremiumProductId.isEmpty() || Regex("^[A-Za-z0-9._-]{3,256}$").matches(streamVuePremiumProductId)) {
    "streamVuePremiumProductId must be empty or a valid seller-console product identifier."
}
val streamVuePremiumVerificationUrl = providers.gradleProperty("streamVuePremiumVerificationUrl")
    .orElse("")
    .get()
    .trim()
if (streamVuePremiumVerificationUrl.isNotEmpty()) {
    val endpoint = URI(streamVuePremiumVerificationUrl)
    require(endpoint.scheme.equals("https", ignoreCase = true) &&
        !endpoint.host.isNullOrBlank() && endpoint.userInfo == null &&
        endpoint.query == null && endpoint.fragment == null) {
        "streamVuePremiumVerificationUrl must be an HTTPS origin/path without credentials, query, or fragment."
    }
}

fun quotedBuildConfig(value: String): String =
    "\"${value.replace("\\", "\\\\").replace("\"", "\\\"")}\""

android {
    namespace = "com.streamvue.player"
    compileSdk {
        version = release(37) {
            minorApiLevel = 1
        }
    }
    buildToolsVersion = "37.0.0"

    defaultConfig {
        applicationId = "com.streamvue.player"
        minSdk = 26
        targetSdk = 36
        versionCode = 5_000_001
        versionName = "5.0.0-alpha.1"
        buildConfigField("String", "DISTRIBUTION_MODE", quotedBuildConfig(streamVueDistributionMode))
        buildConfigField("String", "PREMIUM_PRODUCT_ID", quotedBuildConfig(streamVuePremiumProductId))
        buildConfigField("String", "PREMIUM_VERIFICATION_URL", quotedBuildConfig(streamVuePremiumVerificationUrl))

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        vectorDrawables.useSupportLibrary = true
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
        }
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    testOptions {
        unitTests.all {
            it.useJUnit()
        }
    }

    sourceSets {
        getByName("test").resources.directories.add(
            rootProject.file("../../contracts/fixtures").absolutePath
        )
    }

    packaging {
        resources.excludes += "/META-INF/{AL2.0,LGPL2.1}"
    }
}

dependencies {
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.lifecycle.runtime.ktx)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(libs.androidx.lifecycle.viewmodel.compose)

    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.compose.foundation)
    implementation(libs.androidx.compose.material3)
    implementation(libs.androidx.compose.material.icons)
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.ui.tooling.preview)
    implementation(libs.androidx.tv.material)

    implementation(libs.androidx.media3.exoplayer)
    implementation(libs.androidx.media3.exoplayer.hls)
    implementation(libs.androidx.media3.exoplayer.rtsp)
    implementation(libs.androidx.media3.ui)
    implementation(libs.kotlinx.coroutines.android)
    implementation(libs.gson)
    implementation(libs.google.play.billing)

    testImplementation(libs.junit)
    debugImplementation(libs.androidx.compose.ui.tooling)
}
