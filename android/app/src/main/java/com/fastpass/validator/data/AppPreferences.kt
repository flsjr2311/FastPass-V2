package com.fastpass.validator.data

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import com.fastpass.validator.BuildConfig
import kotlinx.coroutines.flow.first
import java.util.UUID

private val Context.dataStore: DataStore<Preferences> by preferencesDataStore(name = "fastpass_prefs")

/**
 * Persistência leve de configuração e sessão.
 *  - baseUrl: endereço da API (editável na tela de login)
 *  - sessionCookie: cookie fp_token bruto (Set-Cookie), mantém o login entre execuções
 *  - deviceInstallId: UUID gerado uma vez por instalação
 *  - deviceLabel: nome amigável do aparelho, editável
 */
class AppPreferences(private val context: Context) {

    private object Keys {
        val BASE_URL = stringPreferencesKey("base_url")
        val SERVER_CONFIGURED = stringPreferencesKey("server_configured")
        val SESSION_COOKIE = stringPreferencesKey("session_cookie")
        val DEVICE_INSTALL_ID = stringPreferencesKey("device_install_id")
        val DEVICE_LABEL = stringPreferencesKey("device_label")
    }

    suspend fun baseUrl(): String =
        context.dataStore.data.first()[Keys.BASE_URL] ?: BuildConfig.DEFAULT_API_BASE_URL

    suspend fun setBaseUrl(value: String) {
        context.dataStore.edit { it[Keys.BASE_URL] = normalizeBaseUrl(value) }
    }

    /** true depois que o usuário configurou/testou o servidor pela primeira vez. */
    suspend fun isServerConfigured(): Boolean =
        context.dataStore.data.first()[Keys.SERVER_CONFIGURED] == "1"

    suspend fun setServerConfigured(value: Boolean) {
        context.dataStore.edit {
            if (value) it[Keys.SERVER_CONFIGURED] = "1" else it.remove(Keys.SERVER_CONFIGURED)
        }
    }

    suspend fun sessionCookie(): String? =
        context.dataStore.data.first()[Keys.SESSION_COOKIE]

    suspend fun setSessionCookie(value: String?) {
        context.dataStore.edit {
            if (value.isNullOrBlank()) it.remove(Keys.SESSION_COOKIE) else it[Keys.SESSION_COOKIE] = value
        }
    }

    /** Retorna o id de instalação, criando-o na primeira chamada. */
    suspend fun deviceInstallId(): String {
        val existing = context.dataStore.data.first()[Keys.DEVICE_INSTALL_ID]
        if (!existing.isNullOrBlank()) return existing
        val generated = UUID.randomUUID().toString()
        context.dataStore.edit { it[Keys.DEVICE_INSTALL_ID] = generated }
        return generated
    }

    suspend fun deviceLabel(): String? =
        context.dataStore.data.first()[Keys.DEVICE_LABEL]

    suspend fun setDeviceLabel(value: String) {
        context.dataStore.edit { it[Keys.DEVICE_LABEL] = value.trim() }
    }

    companion object {
        fun normalizeBaseUrl(raw: String): String {
            var v = raw.trim()
            if (v.isEmpty()) return v
            if (!v.startsWith("http://") && !v.startsWith("https://")) v = "http://$v"
            return v.trimEnd('/')
        }
    }
}
