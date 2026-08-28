package com.fastpass.validator.data.net

import com.fastpass.validator.data.model.EventDto
import com.fastpass.validator.data.model.GateDto
import com.fastpass.validator.data.model.LoginRequest
import com.fastpass.validator.data.model.SectorDto
import com.fastpass.validator.data.model.SessionDto
import com.fastpass.validator.data.model.ValidateRequest
import com.fastpass.validator.data.model.ValidateResult
import retrofit2.Response
import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.POST
import retrofit2.http.Path
import retrofit2.http.Query

/** Contrato HTTP da API FastPass consumido pelo app. */
interface FastPassApi {

    @POST("api/auth/login")
    suspend fun login(@Body body: LoginRequest): Response<SessionDto>

    @GET("api/auth/me")
    suspend fun me(): Response<SessionDto>

    @POST("api/auth/logout")
    suspend fun logout(): Response<Unit>

    @GET("api/events")
    suspend fun listEvents(): Response<List<EventDto>>

    @GET("api/events/{eventId}/gates")
    suspend fun listGates(
        @Path("eventId") eventId: String,
        @Query("activeOnly") activeOnly: Boolean = true,
    ): Response<List<GateDto>>

    @GET("api/events/{eventId}/sectors")
    suspend fun listSectors(
        @Path("eventId") eventId: String,
        @Query("activeOnly") activeOnly: Boolean = true,
    ): Response<List<SectorDto>>

    @POST("api/access/app/validate")
    suspend fun validate(@Body body: ValidateRequest): Response<ValidateResult>
}
