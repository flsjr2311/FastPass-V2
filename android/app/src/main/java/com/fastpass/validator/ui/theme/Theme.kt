package com.fastpass.validator.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

val FastPassBlue = Color(0xFF5272DE)
val FastPassNavy = Color(0xFF1B2940)
val ApprovedGreen = Color(0xFF1EAE7E)
val RejectedRed = Color(0xFFE0576A)

private val LightColors = lightColorScheme(
    primary = FastPassBlue,
    onPrimary = Color.White,
    secondary = FastPassNavy,
    background = Color(0xFFF4F6FB),
    surface = Color.White,
)

private val DarkColors = darkColorScheme(
    primary = FastPassBlue,
    onPrimary = Color.White,
    secondary = Color(0xFF9FB2F0),
    background = Color(0xFF11192A),
    surface = Color(0xFF17223A),
)

@Composable
fun FastPassTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit,
) {
    MaterialTheme(
        colorScheme = if (darkTheme) DarkColors else LightColors,
        content = content,
    )
}
