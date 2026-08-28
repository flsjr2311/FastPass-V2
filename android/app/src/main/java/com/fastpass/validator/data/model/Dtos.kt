package com.fastpass.validator.data.model

import com.squareup.moshi.Json
import com.squareup.moshi.JsonClass

/** Payload de login. */
@JsonClass(generateAdapter = true)
data class LoginRequest(
    val userName: String,
    val password: String,
)

/** Sessão retornada por /api/auth/login e /api/auth/me. */
@JsonClass(generateAdapter = true)
data class SessionDto(
    val userId: String,
    val userName: String,
    val displayName: String,
    val active: Boolean,
    val permissions: List<String> = emptyList(),
)

/** Evento (subset usado pelo app). */
@JsonClass(generateAdapter = true)
data class EventDto(
    val id: String,
    val name: String,
    val status: String? = null,
    val code: Int? = null,
)

/** Portaria. */
@JsonClass(generateAdapter = true)
data class GateDto(
    val id: String,
    val name: String,
    val operationMode: String? = null,
    @Json(name = "codeNum") val codeNum: Int? = null,
)

/** Setor. */
@JsonClass(generateAdapter = true)
data class SectorDto(
    val id: String,
    val name: String,
    @Json(name = "codeNum") val codeNum: Int? = null,
)

/** Comando de validação (espelha ValidateAccessCommand da API). */
@JsonClass(generateAdapter = true)
data class ValidateRequest(
    val credentialCode: String,
    val eventId: String,
    val gateId: String,
    val sectorId: String? = null,
    val direction: String? = null,
    val idempotencyKey: String,
    val appDeviceLabel: String? = null,
    val appDeviceInstallId: String? = null,
)

/** Resultado da validação (subset relevante para o app). */
@JsonClass(generateAdapter = true)
data class ValidateResult(
    val attemptId: String,
    val approved: Boolean,
    val decision: String,
    val credentialType: String,
    val reason: String? = null,
    val idempotentReplay: Boolean = false,
    val channel: String = "App",
    val direction: String = "Entry",
    val maximumEntries: Int? = null,
    val entriesUsed: Int? = null,
    val peopleInside: Int? = null,
    val message: String? = null,
)

/** Erro padrão da API: { "error": "..." }. */
@JsonClass(generateAdapter = true)
data class ApiError(
    val error: String? = null,
)
