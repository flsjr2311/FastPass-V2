package com.fastpass.validator.data.net

import com.fastpass.validator.data.AppPreferences
import com.squareup.moshi.Moshi
import com.squareup.moshi.kotlin.reflect.KotlinJsonAdapterFactory
import okhttp3.OkHttpClient
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Retrofit
import retrofit2.converter.moshi.MoshiConverterFactory
import java.util.concurrent.TimeUnit

/**
 * Cria instâncias de FastPassApi ligadas a uma baseUrl. Como a baseUrl pode
 * mudar (tela de login), a API é reconstruída quando o endereço muda.
 */
class ApiProvider(private val prefs: AppPreferences) {

    private var cachedBaseUrl: String? = null
    private var cachedApi: FastPassApi? = null

    private val moshi: Moshi = Moshi.Builder()
        .add(KotlinJsonAdapterFactory())
        .build()

    suspend fun api(): FastPassApi {
        val baseUrl = prefs.baseUrl()
        val current = cachedApi
        if (current != null && baseUrl == cachedBaseUrl) return current

        val logging = HttpLoggingInterceptor().apply { level = HttpLoggingInterceptor.Level.BASIC }
        val client = OkHttpClient.Builder()
            .addInterceptor(SessionCookieInterceptor(prefs))
            .addInterceptor(logging)
            .connectTimeout(15, TimeUnit.SECONDS)
            .readTimeout(20, TimeUnit.SECONDS)
            .build()

        val retrofit = Retrofit.Builder()
            .baseUrl(ensureTrailingSlash(baseUrl))
            .client(client)
            .addConverterFactory(MoshiConverterFactory.create(moshi))
            .build()

        val built = retrofit.create(FastPassApi::class.java)
        cachedBaseUrl = baseUrl
        cachedApi = built
        return built
    }

    /** Força a recriação na próxima chamada (após trocar a baseUrl). */
    fun invalidate() {
        cachedApi = null
        cachedBaseUrl = null
    }

    private fun ensureTrailingSlash(url: String): String =
        if (url.endsWith("/")) url else "$url/"
}
