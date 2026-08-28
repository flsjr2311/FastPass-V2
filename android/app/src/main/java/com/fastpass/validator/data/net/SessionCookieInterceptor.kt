package com.fastpass.validator.data.net

import com.fastpass.validator.data.AppPreferences
import kotlinx.coroutines.runBlocking
import okhttp3.Interceptor
import okhttp3.Response

/**
 * Anexa o cookie de sessão salvo às requisições e captura o Set-Cookie do login.
 * Guarda apenas o par nome=valor do cookie fp_token.
 */
class SessionCookieInterceptor(private val prefs: AppPreferences) : Interceptor {

    override fun intercept(chain: Interceptor.Chain): Response {
        val stored = runBlocking { prefs.sessionCookie() }
        val requestBuilder = chain.request().newBuilder()
        if (!stored.isNullOrBlank()) {
            requestBuilder.header("Cookie", stored)
        }
        val response = chain.proceed(requestBuilder.build())

        // Captura o cookie de sessão devolvido pela API (ex.: no login).
        val setCookies = response.headers("Set-Cookie")
        for (raw in setCookies) {
            val pair = raw.substringBefore(';').trim()
            if (pair.startsWith("fp_token=", ignoreCase = true)) {
                val value = pair.substringAfter('=')
                runBlocking {
                    if (value.isBlank() || value.equals("deleted", ignoreCase = true)) {
                        prefs.setSessionCookie(null)
                    } else {
                        prefs.setSessionCookie(pair)
                    }
                }
            }
        }
        return response
    }
}
