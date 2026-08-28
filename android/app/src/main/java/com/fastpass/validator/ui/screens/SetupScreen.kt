package com.fastpass.validator.ui.screens

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fastpass.validator.data.model.EventDto
import com.fastpass.validator.data.model.GateDto
import com.fastpass.validator.ui.UiState

@Composable
fun SetupScreen(
    state: UiState,
    onSelectEvent: (EventDto) -> Unit,
    onSelectGate: (GateDto) -> Unit,
    onStart: () -> Unit,
    onLogout: () -> Unit,
    onOpenSettings: () -> Unit,
) {
    Column(modifier = Modifier.fillMaxSize().padding(20.dp)) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Column {
                Text("Onde você está validando?", fontSize = 20.sp, fontWeight = FontWeight.Bold)
                state.session?.let { Text("Operador: ${it.displayName}", fontSize = 12.sp) }
            }
            TextButton(onClick = onLogout) { Text("Sair") }
        }
        Spacer(Modifier.height(16.dp))

        Text("EVENTO", fontSize = 11.sp, fontWeight = FontWeight.Bold)
        Spacer(Modifier.height(6.dp))
        LazyColumn(modifier = Modifier.fillMaxWidth().weight(1f)) {
            items(state.events) { ev ->
                SelectableRow(
                    title = ev.name,
                    subtitle = ev.status,
                    selected = state.selectedEvent?.id == ev.id,
                    onClick = { onSelectEvent(ev) },
                )
            }
            if (state.selectedEvent != null) {
                item {
                    Spacer(Modifier.height(14.dp))
                    Text("PORTARIA", fontSize = 11.sp, fontWeight = FontWeight.Bold)
                    Spacer(Modifier.height(6.dp))
                }
                items(state.gates) { g ->
                    SelectableRow(
                        title = g.name,
                        subtitle = g.operationMode,
                        selected = state.selectedGate?.id == g.id,
                        onClick = { onSelectGate(g) },
                    )
                }
            }
        }

        state.error?.let {
            Text(it, color = MaterialTheme.colorScheme.error, fontSize = 12.sp)
            Spacer(Modifier.height(8.dp))
        }

        Button(
            onClick = onStart,
            enabled = state.selectedEvent != null && state.selectedGate != null && !state.busy,
            modifier = Modifier.fillMaxWidth().height(52.dp),
        ) {
            Text("Iniciar validação")
        }
        Spacer(Modifier.height(6.dp))
        TextButton(onClick = onOpenSettings, modifier = Modifier.fillMaxWidth()) {
            Text("Configurações do aparelho")
        }
    }
}

@Composable
private fun SelectableRow(title: String, subtitle: String?, selected: Boolean, onClick: () -> Unit) {
    Card(
        modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp).clickable(onClick = onClick),
        colors = CardDefaults.cardColors(
            containerColor = if (selected) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.surface,
        ),
    ) {
        Column(Modifier.padding(14.dp)) {
            Text(
                title,
                fontWeight = FontWeight.SemiBold,
                color = if (selected) Color.White else MaterialTheme.colorScheme.onSurface,
            )
            if (!subtitle.isNullOrBlank()) {
                Text(
                    subtitle,
                    fontSize = 11.sp,
                    color = if (selected) Color.White else MaterialTheme.colorScheme.onSurface,
                )
            }
        }
    }
}
