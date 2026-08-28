package com.fastpass.validator.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fastpass.validator.ui.UiState

/**
 * Primeira tela na instalação: define o endereço do servidor (rede local).
 * Testa a conexão antes de liberar o login.
 */
@Composable
fun ServerSetupScreen(
    state: UiState,
    onTest: (String) -> Unit,
    onCancel: (() -> Unit)? = null,
) {
    var url by remember { mutableStateOf(state.baseUrl) }

    Column(
        modifier = Modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text("FastPass", fontSize = 32.sp, fontWeight = FontWeight.ExtraBold)
        Spacer(Modifier.height(8.dp))
        Text("Configurar servidor", fontSize = 18.sp, fontWeight = FontWeight.SemiBold)
        Spacer(Modifier.height(8.dp))
        Text(
            "Informe o endereço da API na sua rede local. Ex.: 192.168.0.10:5088 " +
                "(o app assume http:// se você não digitar).",
            fontSize = 12.sp,
        )
        Spacer(Modifier.height(24.dp))

        OutlinedTextField(
            value = url,
            onValueChange = { url = it },
            label = { Text("Endereço do servidor") },
            placeholder = { Text("http://192.168.0.10:5088") },
            singleLine = true,
            enabled = !state.testingConnection,
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(Modifier.height(18.dp))

        Button(
            onClick = { onTest(url) },
            enabled = !state.testingConnection && url.isNotBlank(),
            modifier = Modifier.fillMaxWidth().height(52.dp),
        ) {
            if (state.testingConnection) {
                CircularProgressIndicator(modifier = Modifier.height(20.dp))
            } else {
                Text("Testar e continuar")
            }
        }

        state.error?.let {
            Spacer(Modifier.height(16.dp))
            Text(it, color = MaterialTheme.colorScheme.error, fontSize = 13.sp)
        }

        if (onCancel != null) {
            Spacer(Modifier.height(12.dp))
            androidx.compose.material3.TextButton(onClick = onCancel, enabled = !state.testingConnection) {
                Text("Voltar")
            }
        }
    }
}
