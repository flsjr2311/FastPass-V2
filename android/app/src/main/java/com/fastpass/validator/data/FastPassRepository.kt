package com.fastpass.validator.data

import com.fastpass.validator.data.model.EventDto
import com.fastpass.validator.data.model.GateDto
import com.fastpass.validator.data.model.LoginRequest
import com.fastpass.validator.data.model.SectorDto
import com.fastpass.validator.data.model.SessionDto
import com.fastpass.validator.data.model.ValidateRequest
import com.fastpass.validator.data.model.ValidateResult
import com.fastpass.validator.data.net.ApiProvider
import com.squareup.moshi.Moshi
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import org.json.JSONObject
import retrofit2.Response
import java.util.UUID

/** Resultado simples de operações de rede. */
sealed interface Outcome<out T> {
    data class Ok<T>(val value: T) : Outcome<T>
    data class Error(val message: String) : Outcome<Nothing>
}

/**
 * Fonte única de acesso à API. Cuida de sessão, identificação de device e
 * traduz respostas HTTP em Outcome.
 */
class FastPassRepository(
    private val provider: ApiProvider,
    private val prefs: AppPreferences,
) {
    val ACCESS_VALIDATE_PERMISSION = "acesso.validar"

    suspend fun baseUrl(): String = prefs.baseUrl()

    suspend fun updateBaseUrl(value: String) {
        prefs.setBaseUrl(value)
        provider.invalidate()
    }

    suspend fun deviceLabel(): String? = prefs.deviceLabel()
    suspend fun setDeviceLabel(value: String) = prefs.setDeviceLabel(value)

    suspend fun login(userName: String, password: String): Outcome<SessionDto> = call {
        provider.api().login(LoginRequest(userName.trim(), password))
    }

    suspend fun currentSession(): Outcome<SessionDto> = call { provider.api().me() }

    suspend fun logout(): Outcome<Unit> {
        val result = call { provider.api().logout() }
        prefs.setSessionCookie(null)
        return result
    }

    suspend fun listEvents(): Outcome<List<EventDto>> = call { provider.api().listEvents() }

    suspend fun listGates(eventId: String): Outcome<List<GateDto>> =
        call { provider.api().listGates(eventId) }

    suspend fun listSectors(eventId: String): Outcome<List<SectorDto>> =
        call { provider.api().listSectors(eventId) }

    /** Valida um código lido. Gera idempotencyKey e anexa a identificação do aparelho. */
    suspend fun validate(
        credentialCode: String,
        eventId: String,
        gateId: String,
        sectorId: String?,
    ): Outcome<ValidateResult> {
        val installId = prefs.deviceInstallId()
        val label = prefs.deviceLabel()
        val request = ValidateRequest(
            credentialCode = credentialCode.trim(),
            eventId = eventId,
            gateId = gateId,
            sectorId = sectorId,
            direction = null,
            idempotencyKey = UUID.randomUUID().toString(),
            appDeviceLabel = label,
            appDeviceInstallId = installId,
        )
        return call { provider.api().validate(request) }
    }

    private suspend fun <T> call(block: suspend () -> Response<T>): Outcome<T> =
        withContext(Dispatchers.IO) {
            try {
                val response = block()
                if (response.isSuccessful) {
                    @Suppress("UNCHECKED_CAST")
                    val body = (response.body() ?: Unit) as T
                    Outcome.Ok(body)
                } else {
                    Outcome.Error(parseError(response))
                }
            } catch (e: Exception) {
                Outcome.Error(e.message ?: "Falha de conexão. Verifique o endereço da API e a rede.")
            }
        }

    private fun parseError(response: Response<*>): String {
        val raw = try {
            response.errorBody()?.string()
        } catch (_: Exception) {
            null
        }
        if (response.code() == 401) return "Sessão expirada ou não autenticada. Faça login novamente."
        if (response.code() == 403) return "Sem permissão para validar acesso neste evento/portaria."
        if (!raw.isNullOrBlank()) {
            try {
                val json = JSONObject(raw)
                if (json.has("error")) return json.getString("error")
            } catch (_: Exception) { /* corpo não-JSON */ }
        }
        return "Erro HTTP ${response.code()}."
    }
}
