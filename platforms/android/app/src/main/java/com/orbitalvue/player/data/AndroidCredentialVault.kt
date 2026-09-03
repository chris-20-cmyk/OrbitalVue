package com.orbitalvue.player.data

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import androidx.core.content.edit
import com.google.gson.Gson
import com.google.gson.annotations.SerializedName
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

internal interface MediaCenterCredentialVault {
    fun save(id: String, value: String)
    fun read(id: String): String?
    fun remove(id: String)
}

/** Encrypts media-center credentials with a non-exportable Android Keystore key. */
internal class AndroidKeystoreCredentialVault(
    context: Context,
    private val gson: Gson = Gson()
) : MediaCenterCredentialVault {
    private val preferences = context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE)
    private val lock = Any()

    override fun save(id: String, value: String) = synchronized(lock) {
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(Cipher.ENCRYPT_MODE, key())
        val envelope = EncryptedEnvelope(
            iv = Base64.encodeToString(cipher.iv, Base64.NO_WRAP),
            ciphertext = Base64.encodeToString(
                cipher.doFinal(value.toByteArray(Charsets.UTF_8)),
                Base64.NO_WRAP
            )
        )
        preferences.edit(commit = true) { putString(storageKey(id), gson.toJson(envelope)) }
    }

    override fun read(id: String): String? = synchronized(lock) {
        val raw = preferences.getString(storageKey(id), null) ?: return@synchronized null
        val envelope = runCatching { gson.fromJson(raw, EncryptedEnvelope::class.java) }
            .getOrElse { error("The protected media-center credential is damaged.") }
        val cipher = Cipher.getInstance(TRANSFORMATION)
        cipher.init(
            Cipher.DECRYPT_MODE,
            key(),
            GCMParameterSpec(128, Base64.decode(envelope.iv, Base64.NO_WRAP))
        )
        val plaintext = runCatching {
            cipher.doFinal(Base64.decode(envelope.ciphertext, Base64.NO_WRAP))
        }.getOrElse { error("Android could not unlock the protected media-center credential.") }
        String(plaintext, Charsets.UTF_8)
    }

    override fun remove(id: String) {
        preferences.edit(commit = true) { remove(storageKey(id)) }
    }

    private fun key(): SecretKey {
        val keyStore = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (keyStore.getKey(KEY_ALIAS, null) as? SecretKey)?.let { return it }
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore").run {
            init(
                KeyGenParameterSpec.Builder(
                    KEY_ALIAS,
                    KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT
                )
                    .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                    .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                    .setRandomizedEncryptionRequired(true)
                    .build()
            )
            generateKey()
        }
    }

    private fun storageKey(id: String): String = "credential-${MediaCenterUrlPolicy.hash(id).take(48)}"

    private data class EncryptedEnvelope(
        @SerializedName("version") val version: Int = 1,
        @SerializedName("iv") val iv: String,
        @SerializedName("ciphertext") val ciphertext: String
    )

    private companion object {
        const val PREFERENCES = "orbitalvue-media-credentials-v1"
        const val KEY_ALIAS = "com.orbitalvue.player.media-center.v1"
        const val TRANSFORMATION = "AES/GCM/NoPadding"
    }
}
