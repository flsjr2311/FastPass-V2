package com.fastpass.validator

import android.Manifest
import android.content.pm.PackageManager
import android.media.AudioManager
import android.media.ToneGenerator
import android.os.Build
import android.os.Bundle
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import android.view.KeyEvent
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.fastpass.validator.ui.MainViewModel
import com.fastpass.validator.ui.Screen
import com.fastpass.validator.ui.screens.LoginScreen
import com.fastpass.validator.ui.screens.ScannerScreen
import com.fastpass.validator.ui.screens.SettingsScreen
import com.fastpass.validator.ui.screens.SetupScreen
import com.fastpass.validator.ui.theme.FastPassTheme

class MainActivity : ComponentActivity() {

    private var viewModelRef: MainViewModel? = null

    // Buffer para o leitor USB-C (HID): acumula caracteres até o Enter.
    private val hidBuffer = StringBuilder()

    private val hasCameraPermission = mutableStateOf(false)

    private val cameraPermissionLauncher =
        registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
            hasCameraPermission.value = granted
        }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        hasCameraPermission.value = ContextCompat.checkSelfPermission(
            this, Manifest.permission.CAMERA,
        ) == PackageManager.PERMISSION_GRANTED

        setContent {
            val vm: MainViewModel = viewModel()
            viewModelRef = vm
            val state by vm.state.collectAsStateWithLifecycle()
            val camera by hasCameraPermission

            // Pede câmera ao entrar no scanner, se ainda não tiver.
            if (state.screen == Screen.Scanner && !camera) {
                requestCameraOnce()
            }

            // Dispara som + vibração sempre que um novo resultado chega.
            androidx.compose.runtime.LaunchedEffect(state.lastResult?.attemptId) {
                state.lastResult?.let { feedback(it.approved) }
            }

            FastPassTheme {
                when (state.screen) {
                    Screen.Loading -> LoadingScreen()
                    Screen.Login -> LoginScreen(
                        state = state,
                        onLogin = vm::login,
                        onOpenSettings = vm::openSettings,
                    )
                    Screen.Setup -> SetupScreen(
                        state = state,
                        onSelectEvent = vm::selectEvent,
                        onSelectGate = vm::selectGate,
                        onStart = vm::startScanning,
                        onLogout = vm::logout,
                        onOpenSettings = vm::openSettings,
                    )
                    Screen.Scanner -> ScannerScreen(
                        state = state,
                        hasCameraPermission = camera,
                        onRequestCamera = { requestCameraOnce() },
                        onBack = vm::backToSetup,
                        onCode = { code -> handleCode(vm, code) },
                    )
                    Screen.Settings -> SettingsScreen(
                        state = state,
                        onSaveBaseUrl = vm::setBaseUrl,
                        onSaveDeviceLabel = vm::setDeviceLabel,
                        onClose = vm::closeSettings,
                    )
                }
            }
        }
    }

    private var cameraRequested = false
    private fun requestCameraOnce() {
        if (cameraRequested) return
        cameraRequested = true
        cameraPermissionLauncher.launch(Manifest.permission.CAMERA)
    }

    private fun handleCode(vm: MainViewModel, code: String) {
        vm.onCodeScanned(code)
        // Feedback sensorial é disparado no dispatch do resultado (observado abaixo).
    }

    /**
     * Captura teclas físicas do leitor USB-C (que se comporta como teclado HID).
     * Acumula caracteres imprimíveis e dispara a validação ao receber Enter.
     */
    override fun dispatchKeyEvent(event: KeyEvent): Boolean {
        val vm = viewModelRef
        val state = vm?.state?.value
        if (vm != null && state?.screen == Screen.Scanner && event.action == KeyEvent.ACTION_DOWN) {
            when (event.keyCode) {
                KeyEvent.KEYCODE_ENTER, KeyEvent.KEYCODE_NUMPAD_ENTER -> {
                    val code = hidBuffer.toString().trim()
                    hidBuffer.setLength(0)
                    if (code.isNotEmpty()) {
                        vm.onCodeScanned(code)
                        return true
                    }
                }
                else -> {
                    val ch = event.unicodeChar
                    // Só captura de teclado físico (leitor USB-C). Teclado virtual usa deviceId == -1.
                    if (ch != 0 && event.deviceId > 0) {
                        hidBuffer.append(ch.toChar())
                        return true
                    }
                }
            }
        }
        return super.dispatchKeyEvent(event)
    }



    // Feedback sonoro/vibração — chamado pela própria Activity ao observar resultado.
    fun feedback(approved: Boolean) {
        try {
            val tone = ToneGenerator(AudioManager.STREAM_MUSIC, 90)
            tone.startTone(
                if (approved) ToneGenerator.TONE_PROP_ACK else ToneGenerator.TONE_PROP_NACK,
                200,
            )
        } catch (_: Exception) { }

        val pattern = if (approved) longArrayOf(0, 120) else longArrayOf(0, 120, 100, 120)
        try {
            val vibrator = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                (getSystemService(VIBRATOR_MANAGER_SERVICE) as VibratorManager).defaultVibrator
            } else {
                @Suppress("DEPRECATION")
                getSystemService(VIBRATOR_SERVICE) as Vibrator
            }
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                vibrator.vibrate(VibrationEffect.createWaveform(pattern, -1))
            } else {
                @Suppress("DEPRECATION")
                vibrator.vibrate(pattern, -1)
            }
        } catch (_: Exception) { }
    }
}

@Composable
private fun LoadingScreen() {
    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        CircularProgressIndicator()
    }
}
