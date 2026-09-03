import java.net.URI

plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
}

val orbitalVueDistributionMode = providers.gradleProperty("orbitalVueDistributionMode")
    .orElse("personal")
    .get()
    .trim()
    .lowercase()
require(orbitalVueDistributionMode == "personal" || orbitalVueDistributionMode == "store") {
    "orbitalVueDistributionMode must be personal or store."
}
val orbitalVuePremiumProductId = providers.gradleProperty("orbitalVuePremiumProductId")
    .orElse("")
    .get()
    .trim()
require(orbitalVuePremiumProductId.isEmpty() || Regex("^[A-Za-z0-9._-]{3,256}$").matches(orbitalVuePremiumProductId)) {
    "orbitalVuePremiumProductId must be empty or a valid seller-console product identifier."
}
val orbitalVuePremiumVerificationUrl = providers.gradleProperty("orbitalVuePremiumVerificationUrl")
    .orElse("")
    .get()
    .trim()
if (orbitalVuePremiumVerificationUrl.isNotEmpty()) {
    val endpoint = URI(orbitalVuePremiumVerificationUrl)
    require(endpoint.scheme.equals("https", ignoreCase = true) &&
        !endpoint.host.isNullOrBlank() && endpoint.userInfo == null &&
        endpoint.query == null && endpoint.fragment == null) {
        "orbitalVuePremiumVerificationUrl must be an HTTPS origin/path without credentials, query, or fragment."
    }
}

val orbitalVueVersionCodeText = providers.gradleProperty("orbitalVueVersionCode")
    .orElse("5700001")
    .get()
    .trim()
val orbitalVueVersionCode = orbitalVueVersionCodeText.toIntOrNull()
require(orbitalVueVersionCode != null && orbitalVueVersionCode in 1..2_100_000_000) {
    "orbitalVueVersionCode must be a positive integer no greater than Google Play's 2100000000 limit."
}
val orbitalVueVersionName = providers.gradleProperty("orbitalVueVersionName")
    .orElse("5.8.0-alpha.5")
    .get()
    .trim()
require(Regex("^[0-9A-Za-z][0-9A-Za-z._+-]{0,99}$").matches(orbitalVueVersionName)) {
    "orbitalVueVersionName must be a non-empty release label using letters, digits, dot, underscore, plus, or hyphen."
}

val orbitalVueRequireStoreSigningText = providers.gradleProperty("orbitalVueRequireStoreSigning")
    .orElse("false")
    .get()
    .trim()
    .lowercase()
require(orbitalVueRequireStoreSigningText == "true" || orbitalVueRequireStoreSigningText == "false") {
    "orbitalVueRequireStoreSigning must be true or false."
}
val orbitalVueRequireStoreSigning = orbitalVueRequireStoreSigningText == "true"
val orbitalVueUploadKeystorePath = System.getenv("ORBITALVUE_ANDROID_KEYSTORE_PATH")?.trim().orEmpty()
val orbitalVueUploadStorePassword = System.getenv("ORBITALVUE_ANDROID_KEYSTORE_PASSWORD").orEmpty()
val orbitalVueUploadKeyAlias = System.getenv("ORBITALVUE_ANDROID_KEY_ALIAS")?.trim().orEmpty()
val orbitalVueUploadKeyPassword = System.getenv("ORBITALVUE_ANDROID_KEY_PASSWORD").orEmpty()
val orbitalVueSigningValues = listOf(
    orbitalVueUploadKeystorePath,
    orbitalVueUploadStorePassword,
    orbitalVueUploadKeyAlias,
    orbitalVueUploadKeyPassword
)
val orbitalVueHasAnySigningValue = orbitalVueSigningValues.any(String::isNotEmpty)
val orbitalVueHasCompleteSigningValues = orbitalVueSigningValues.all(String::isNotEmpty)
require(!orbitalVueHasAnySigningValue || orbitalVueHasCompleteSigningValues) {
    "Android release signing is incomplete. Supply all four ORBITALVUE_ANDROID_KEYSTORE_* / KEY_* environment values."
}
require(!orbitalVueHasAnySigningValue ||
    (orbitalVueDistributionMode == "store" && orbitalVueRequireStoreSigning)) {
    "The protected Google Play upload key may be used only for an explicitly required store candidate."
}
require(orbitalVueUploadKeyAlias.isEmpty() ||
    (orbitalVueUploadKeyAlias.length <= 256 && orbitalVueUploadKeyAlias.none(Char::isISOControl))) {
    "ORBITALVUE_ANDROID_KEY_ALIAS is invalid."
}
if (orbitalVueHasCompleteSigningValues) {
    require(rootProject.file(orbitalVueUploadKeystorePath).isFile) {
        "ORBITALVUE_ANDROID_KEYSTORE_PATH does not identify a readable keystore file."
    }
}
if (orbitalVueRequireStoreSigning) {
    require(orbitalVueDistributionMode == "store") {
        "A required Google Play signing build must use orbitalVueDistributionMode=store."
    }
    require(orbitalVuePremiumProductId.isNotEmpty() && orbitalVuePremiumVerificationUrl.isNotEmpty()) {
        "A signed Google Play candidate requires the exact premium product ID and HTTPS verifier URL."
    }
    require(orbitalVueHasCompleteSigningValues) {
        "A signed Google Play candidate requires the complete upload-keystore environment configuration."
    }
}

fun quotedBuildConfig(value: String): String =
    "\"${value.replace("\\", "\\\\").replace("\"", "\\\"")}\""

android {
    namespace = "com.orbitalvue.player"
    compileSdk {
        version = release(37) {
            minorApiLevel = 1
        }
    }
    buildToolsVersion = "37.0.0"

    defaultConfig {
        applicationId = "com.orbitalvue.player"
        minSdk = 26
        targetSdk = 36
        versionCode = orbitalVueVersionCode
        versionName = orbitalVueVersionName
        buildConfigField("String", "DISTRIBUTION_MODE", quotedBuildConfig(orbitalVueDistributionMode))
        buildConfigField("String", "PREMIUM_PRODUCT_ID", quotedBuildConfig(orbitalVuePremiumProductId))
        buildConfigField("String", "PREMIUM_VERIFICATION_URL", quotedBuildConfig(orbitalVuePremiumVerificationUrl))

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        vectorDrawables.useSupportLibrary = true
    }

    val orbitalVueReleaseSigning = if (orbitalVueHasCompleteSigningValues) {
        signingConfigs.create("orbitalVueRelease") {
            storeFile = rootProject.file(orbitalVueUploadKeystorePath)
            storePassword = orbitalVueUploadStorePassword
            keyAlias = orbitalVueUploadKeyAlias
            keyPassword = orbitalVueUploadKeyPassword
        }
    } else {
        null
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            signingConfig = orbitalVueReleaseSigning
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
    implementation(libs.tink.android)
    implementation(libs.zxing.core)

    testImplementation(libs.junit)
    debugImplementation(libs.androidx.compose.ui.tooling)
}
