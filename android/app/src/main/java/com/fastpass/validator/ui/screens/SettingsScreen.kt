package com.fastpass.validator.ui.screens

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fastpass.validator.ui.UiState

@Composable
fun SettingsScreen(
    state: UiState,
    onSaveDeviceLabel: (String) -> Unit,
    onClose: () -> Unit,
) {
    var label by remember { mutableStateOf(state.deviceLabel) }

    Column(modifier = Modifier.fillMaxSize().padding(24.dp)) {
        Text("Configurações do aparelho", fontSize = 22.sp, fontWeight = FontWeight.Bold)
        Spacer(Modifier.height(20.dp))

        Text("Servidor", fontWeight = FontWeight.SemiBold)
        Text(state.baseUrl, fontSize = 12.sp)
        Text("Para trocar o servidor, saia e use \"Trocar\" na tela de login.", fontSize = 11.sp)
        Spacer(Modifier.height(20.dp))

        Text("Nome deste aparelho", fontWeight = FontWeight.SemiBold)
        Text("Aparece na auditoria de acessos. Ex.: Portaria A - Celular 1.", fontSize = 11.sp)
        Spacer(Modifier.height(6.dp))
        OutlinedTextField(
            value = label,
            onValueChange = { label = it },
            singleLine = true,
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(Modifier.height(24.dp))

        Button(
            onClick = {
                onSaveDeviceLabel(label)
                onClose()
            },
            modifier = Modifier.fillMaxWidth().height(50.dp),
        ) {
            Text("Salvar")
        }
        Spacer(Modifier.height(8.dp))
        OutlinedButton(onClick = onClose, modifier = Modifier.fillMaxWidth()) {
            Text("Cancelar")
        }
    }
}
