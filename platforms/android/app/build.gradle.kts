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

val streamVueVersionCodeText = providers.gradleProperty("streamVueVersionCode")
    .orElse("5100001")
    .get()
    .trim()
val streamVueVersionCode = streamVueVersionCodeText.toIntOrNull()
require(streamVueVersionCode != null && streamVueVersionCode in 1..2_100_000_000) {
    "streamVueVersionCode must be a positive integer no greater than Google Play's 2100000000 limit."
}
val streamVueVersionName = providers.gradleProperty("streamVueVersionName")
    .orElse("5.1.0-alpha.1")
    .get()
    .trim()
require(Regex("^[0-9A-Za-z][0-9A-Za-z._+-]{0,99}$").matches(streamVueVersionName)) {
    "streamVueVersionName must be a non-empty release label using letters, digits, dot, underscore, plus, or hyphen."
}

val streamVueRequireStoreSigningText = providers.gradleProperty("streamVueRequireStoreSigning")
    .orElse("false")
    .get()
    .trim()
    .lowercase()
require(streamVueRequireStoreSigningText == "true" || streamVueRequireStoreSigningText == "false") {
    "streamVueRequireStoreSigning must be true or false."
}
val streamVueRequireStoreSigning = streamVueRequireStoreSigningText == "true"
val streamVueUploadKeystorePath = System.getenv("STREAMVUE_ANDROID_KEYSTORE_PATH")?.trim().orEmpty()
val streamVueUploadStorePassword = System.getenv("STREAMVUE_ANDROID_KEYSTORE_PASSWORD").orEmpty()
val streamVueUploadKeyAlias = System.getenv("STREAMVUE_ANDROID_KEY_ALIAS")?.trim().orEmpty()
val streamVueUploadKeyPassword = System.getenv("STREAMVUE_ANDROID_KEY_PASSWORD").orEmpty()
val streamVueSigningValues = listOf(
    streamVueUploadKeystorePath,
    streamVueUploadStorePassword,
    streamVueUploadKeyAlias,
    streamVueUploadKeyPassword
)
val streamVueHasAnySigningValue = streamVueSigningValues.any(String::isNotEmpty)
val streamVueHasCompleteSigningValues = streamVueSigningValues.all(String::isNotEmpty)
require(!streamVueHasAnySigningValue || streamVueHasCompleteSigningValues) {
    "Android release signing is incomplete. Supply all four STREAMVUE_ANDROID_KEYSTORE_* / KEY_* environment values."
}
require(!streamVueHasAnySigningValue ||
    (streamVueDistributionMode == "store" && streamVueRequireStoreSigning)) {
    "The protected Google Play upload key may be used only for an explicitly required store candidate."
}
require(streamVueUploadKeyAlias.isEmpty() ||
    (streamVueUploadKeyAlias.length <= 256 && streamVueUploadKeyAlias.none(Char::isISOControl))) {
    "STREAMVUE_ANDROID_KEY_ALIAS is invalid."
}
if (streamVueHasCompleteSigningValues) {
    require(rootProject.file(streamVueUploadKeystorePath).isFile) {
        "STREAMVUE_ANDROID_KEYSTORE_PATH does not identify a readable keystore file."
    }
}
if (streamVueRequireStoreSigning) {
    require(streamVueDistributionMode == "store") {
        "A required Google Play signing build must use streamVueDistributionMode=store."
    }
    require(streamVuePremiumProductId.isNotEmpty() && streamVuePremiumVerificationUrl.isNotEmpty()) {
        "A signed Google Play candidate requires the exact premium product ID and HTTPS verifier URL."
    }
    require(streamVueHasCompleteSigningValues) {
        "A signed Google Play candidate requires the complete upload-keystore environment configuration."
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
        versionCode = streamVueVersionCode
        versionName = streamVueVersionName
        buildConfigField("String", "DISTRIBUTION_MODE", quotedBuildConfig(streamVueDistributionMode))
        buildConfigField("String", "PREMIUM_PRODUCT_ID", quotedBuildConfig(streamVuePremiumProductId))
        buildConfigField("String", "PREMIUM_VERIFICATION_URL", quotedBuildConfig(streamVuePremiumVerificationUrl))

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        vectorDrawables.useSupportLibrary = true
    }

    val streamVueReleaseSigning = if (streamVueHasCompleteSigningValues) {
        signingConfigs.create("streamVueRelease") {
            storeFile = rootProject.file(streamVueUploadKeystorePath)
            storePassword = streamVueUploadStorePassword
            keyAlias = streamVueUploadKeyAlias
            keyPassword = streamVueUploadKeyPassword
        }
    } else {
        null
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            signingConfig = streamVueReleaseSigning
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
