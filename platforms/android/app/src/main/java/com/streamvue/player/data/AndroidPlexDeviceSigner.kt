package com.streamvue.player.data

import android.content.Context
import android.util.Base64
import com.google.crypto.tink.Aead
import com.google.crypto.tink.KeysetHandle
import com.google.crypto.tink.PublicKeySign
import com.google.crypto.tink.RegistryConfiguration
import com.google.crypto.tink.TinkProtoKeysetFormat
import com.google.crypto.tink.integration.android.AndroidKeystore
import com.google.crypto.tink.signature.PredefinedSignatureParameters
import com.google.crypto.tink.signature.SignatureConfig
import com.google.crypto.tink.signature.SignatureJwkSetConverter
import com.google.crypto.tink.subtle.Hex
import com.google.gson.Gson
import com.google.gson.JsonObject
import com.google.gson.JsonParser
import java.security.MessageDigest
import java.util.TreeMap

/**
 * A stable Ed25519 identity whose private keyset is encrypted by an Android
 * Keystore master key. Sign-in fails closed if the device cannot use Keystore.
 */
internal class AndroidPlexDeviceSigner(
    context: Context,
    private val gson: Gson = Gson()
) : PlexDeviceSigner {
    private val handle by lazy(LazyThreadSafetyMode.SYNCHRONIZED) {
        SignatureConfig.register()
        synchronized(KEYSET_LOCK) {
            loadOrCreateEncryptedKeyset(context.applicationContext)
        }
    }
    private val signer: PublicKeySign by lazy(LazyThreadSafetyMode.SYNCHRONIZED) {
        handle.getPrimitive(RegistryConfiguration.get(), PublicKeySign::class.java)
    }

    override val publicJwk: JsonObject by lazy(LazyThreadSafetyMode.SYNCHRONIZED) {
        val set = JsonParser.parseString(
            SignatureJwkSetConverter.fromPublicKeysetHandle(handle.publicKeysetHandle)
        ).asJsonObject
        val exported = set.getAsJsonArray("keys")?.singleOrNull()?.asJsonObject
            ?: error("Android could not export the Plex device public key.")
        val x = exported.get("x")?.asString.orEmpty()
        val publicBytes = runCatching { decodeUrlSafe(x) }
            .getOrElse { error("Android could not export the Plex device public key.") }
        require(publicBytes.size == 32) { "Android exported an invalid Plex device public key." }
        JsonObject().apply {
            addProperty("kty", "OKP")
            addProperty("crv", "Ed25519")
            addProperty("x", x)
            addProperty("kid", encodeUrlSafe(MessageDigest.getInstance("SHA-256").digest(publicBytes)))
            addProperty("alg", "EdDSA")
        }
    }

    override fun sign(claims: Map<String, Any>): String {
        val keyId = publicJwk.get("kid")?.asString.orEmpty()
        val header = sortedMapOf<String, Any>(
            "alg" to "EdDSA",
            "kid" to keyId,
            "typ" to "JWT"
        )
        val encodedHeader = encodedJson(header)
        val encodedClaims = encodedJson(TreeMap(claims))
        val signingInput = "$encodedHeader.$encodedClaims"
        val signature = signer.sign(signingInput.toByteArray(Charsets.UTF_8))
        require(signature.size == 64) { "Android returned an invalid Plex device signature." }
        return "$signingInput.${encodeUrlSafe(signature)}"
    }

    private fun encodedJson(value: Map<String, Any>): String = encodeUrlSafe(
        gson.toJson(value).toByteArray(Charsets.UTF_8)
    )

    private fun encodeUrlSafe(value: ByteArray): String = Base64.encodeToString(
        value,
        Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING
    )

    private fun decodeUrlSafe(value: String): ByteArray = Base64.decode(
        value,
        Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING
    )

    private fun loadOrCreateEncryptedKeyset(context: Context): KeysetHandle {
        val preferences = context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE)
        val storedKeyset = preferences.getString(KEYSET_NAME, null)
        val masterKeyExisted = AndroidKeystore.hasKey(MASTER_KEY_ALIAS)

        if (!masterKeyExisted) {
            AndroidKeystore.generateNewAes256GcmKey(MASTER_KEY_ALIAS)
        }

        val masterAead = AndroidKeystore.getAead(MASTER_KEY_ALIAS)
        verifyMasterKey(masterAead)

        if (masterKeyExisted && storedKeyset != null) {
            return TinkProtoKeysetFormat.parseEncryptedKeyset(
                Hex.decode(storedKeyset),
                masterAead,
                KEYSET_ASSOCIATED_DATA,
                RegistryConfiguration.get()
            )
        }

        val generated = KeysetHandle.generateNew(PredefinedSignatureParameters.ED25519WithRawOutput)
        val encrypted = TinkProtoKeysetFormat.serializeEncryptedKeyset(
            generated,
            masterAead,
            KEYSET_ASSOCIATED_DATA,
            RegistryConfiguration.get()
        )
        check(preferences.edit().putString(KEYSET_NAME, Hex.encode(encrypted)).commit()) {
            "Android could not persist the encrypted Plex sign-in key."
        }
        return generated
    }

    private fun verifyMasterKey(masterAead: Aead) {
        val probe = "streamvue-plex-keystore-check".toByteArray(Charsets.UTF_8)
        val ciphertext = masterAead.encrypt(probe, KEYSET_ASSOCIATED_DATA)
        check(MessageDigest.isEqual(probe, masterAead.decrypt(ciphertext, KEYSET_ASSOCIATED_DATA))) {
            "This device could not verify the Plex sign-in key protection."
        }
    }

    private companion object {
        val KEYSET_LOCK = Any()
        val KEYSET_ASSOCIATED_DATA = "streamvue:plex-device-signing:v1".toByteArray(Charsets.UTF_8)
        const val PREFERENCES = "streamvue-plex-device-signing-v1"
        const val KEYSET_NAME = "plex_ed25519_keyset"
        const val MASTER_KEY_ALIAS = "com.streamvue.player.plex-signing.v1"
    }
}
