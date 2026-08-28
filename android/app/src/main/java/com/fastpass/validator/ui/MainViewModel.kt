package com.fastpass.validator.ui

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.fastpass.validator.data.AppPreferences
import com.fastpass.validator.data.FastPassRepository
import com.fastpass.validator.data.Outcome
import com.fastpass.validator.data.model.EventDto
import com.fastpass.validator.data.model.GateDto
import com.fastpass.validator.data.model.SessionDto
import com.fastpass.validator.data.model.ValidateResult
import com.fastpass.validator.data.net.ApiProvider
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/** Telas do app. */
enum class Screen { Loading, Login, Setup, Scanner, Settings }

/** Item do histórico de validações da sessão atual. */
data class FeedItem(
    val code: String,
    val approved: Boolean,
    val label: String,
    val timeMillis: Long,
)

/** Estado global observável pela UI. */
data class UiState(
    val screen: Screen = Screen.Loading,
    val baseUrl: String = "",
    val session: SessionDto? = null,
    val busy: Boolean = false,
    val error: String? = null,
    // seleção
    val events: List<EventDto> = emptyList(),
    val gates: List<GateDto> = emptyList(),
    val selectedEvent: EventDto? = null,
    val selectedGate: GateDto? = null,
    // validação
    val lastResult: ValidateResult? = null,
    val lastCode: String? = null,
    val validating: Boolean = false,
    val feed: List<FeedItem> = emptyList(),
    val deviceLabel: String = "",
)

class MainViewModel(app: Application) : AndroidViewModel(app) {

    private val prefs = AppPreferences(app)
    private val repo = FastPassRepository(ApiProvider(prefs), prefs)

    private val _state = MutableStateFlow(UiState())
    val state: StateFlow<UiState> = _state.asStateFlow()

    init {
        viewModelScope.launch {
            val base = repo.baseUrl()
            val label = repo.deviceLabel().orEmpty()
            _state.update { it.copy(baseUrl = base, deviceLabel = label) }
            // Tenta reaproveitar uma sessão salva.
            when (val me = repo.currentSession()) {
                is Outcome.Ok -> {
                    _state.update { it.copy(session = me.value) }
                    loadEventsInternal(goToSetup = true)
                }
                is Outcome.Error -> _state.update { it.copy(screen = Screen.Login) }
            }
        }
    }

    fun clearError() = _state.update { it.copy(error = null) }

    fun setBaseUrl(value: String) {
        viewModelScope.launch {
            repo.updateBaseUrl(value)
            _state.update { it.copy(baseUrl = repo.baseUrl()) }
        }
    }

    fun setDeviceLabel(value: String) {
        viewModelScope.launch {
            repo.setDeviceLabel(value)
            _state.update { it.copy(deviceLabel = value) }
        }
    }

    fun login(userName: String, password: String) {
        if (userName.isBlank() || password.isBlank()) {
            _state.update { it.copy(error = "Informe usuário e senha.") }
            return
        }
        viewModelScope.launch {
            _state.update { it.copy(busy = true, error = null) }
            when (val res = repo.login(userName, password)) {
                is Outcome.Ok -> {
                    val session = res.value
                    if (!session.permissions.contains(repo.ACCESS_VALIDATE_PERMISSION)) {
                        repo.logout()
                        _state.update {
                            it.copy(busy = false, error = "Este usuário não tem permissão para validar acesso.")
                        }
                    } else {
                        _state.update { it.copy(busy = false, session = session) }
                        loadEventsInternal(goToSetup = true)
                    }
                }
                is Outcome.Error -> _state.update { it.copy(busy = false, error = res.message) }
            }
        }
    }

    fun logout() {
        viewModelScope.launch {
            repo.logout()
            _state.update {
                UiState(screen = Screen.Login, baseUrl = it.baseUrl, deviceLabel = it.deviceLabel)
            }
        }
    }

    fun reloadEvents() = loadEventsInternal(goToSetup = false)

    private fun loadEventsInternal(goToSetup: Boolean) {
        viewModelScope.launch {
            _state.update { it.copy(busy = true, error = null) }
            when (val res = repo.listEvents()) {
                is Outcome.Ok -> _state.update {
                    it.copy(
                        busy = false,
                        events = res.value,
                        screen = if (goToSetup) Screen.Setup else it.screen,
                    )
                }
                is Outcome.Error -> _state.update {
                    it.copy(busy = false, error = res.message, screen = if (goToSetup) Screen.Setup else it.screen)
                }
            }
        }
    }

    fun selectEvent(event: EventDto) {
        _state.update { it.copy(selectedEvent = event, selectedGate = null, gates = emptyList()) }
        viewModelScope.launch {
            _state.update { it.copy(busy = true, error = null) }
            when (val res = repo.listGates(event.id)) {
                is Outcome.Ok -> _state.update { it.copy(busy = false, gates = res.value) }
                is Outcome.Error -> _state.update { it.copy(busy = false, error = res.message) }
            }
        }
    }

    fun selectGate(gate: GateDto) = _state.update { it.copy(selectedGate = gate) }

    fun startScanning() {
        val s = _state.value
        if (s.selectedEvent == null || s.selectedGate == null) {
            _state.update { it.copy(error = "Escolha o evento e a portaria.") }
            return
        }
        _state.update { it.copy(screen = Screen.Scanner, lastResult = null, lastCode = null, error = null) }
    }

    fun backToSetup() = _state.update { it.copy(screen = Screen.Setup) }
    fun openSettings() = _state.update { it.copy(screen = Screen.Settings) }
    fun closeSettings() {
        val target = if (_state.value.session != null) Screen.Setup else Screen.Login
        _state.update { it.copy(screen = target) }
    }

    /** Chamado ao ler um código (câmera, leitor USB-C ou manual). */
    fun onCodeScanned(rawCode: String) {
        val code = rawCode.trim()
        val s = _state.value
        if (code.isEmpty() || s.validating) return
        val event = s.selectedEvent ?: return
        val gate = s.selectedGate ?: return
        // Evita revalidar o mesmo código imediatamente (anti-duplo-scan).
        if (code == s.lastCode && s.lastResult != null) return

        viewModelScope.launch {
            _state.update { it.copy(validating = true, lastCode = code, error = null) }
            when (val res = repo.validate(code, event.id, gate.id, null)) {
                is Outcome.Ok -> {
                    val r = res.value
                    val item = FeedItem(
                        code = code,
                        approved = r.approved,
                        label = if (r.approved) "Liberado" else (r.reason ?: "Negado"),
                        timeMillis = System.currentTimeMillis(),
                    )
                    _state.update {
                        it.copy(
                            validating = false,
                            lastResult = r,
                            feed = (listOf(item) + it.feed).take(10),
                        )
                    }
                }
                is Outcome.Error -> _state.update {
                    it.copy(validating = false, error = res.message, lastResult = null)
                }
            }
        }
    }

    /** Libera o campo para uma nova leitura após o feedback. */
    fun readyForNext() = _state.update { it.copy(lastResult = null, lastCode = null) }
}
