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
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fastpass.validator.ui.UiState

@Composable
fun LoginScreen(
    state: UiState,
    onLogin: (String, String) -> Unit,
    onOpenSettings: () -> Unit,
) {
    var user by remember { mutableStateOf("") }
    var pass by remember { mutableStateOf("") }

    Column(
        modifier = Modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text("FastPass", fontSize = 34.sp, fontWeight = FontWeight.ExtraBold)
        Text("Validador de acesso", fontSize = 14.sp)
        Spacer(Modifier.height(36.dp))

        OutlinedTextField(
            value = user,
            onValueChange = { user = it },
            label = { Text("Usuário") },
            singleLine = true,
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(Modifier.height(12.dp))
        OutlinedTextField(
            value = pass,
            onValueChange = { pass = it },
            label = { Text("Senha") },
            singleLine = true,
            visualTransformation = PasswordVisualTransformation(),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password),
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(Modifier.height(20.dp))

        Button(
            onClick = { onLogin(user, pass) },
            enabled = !state.busy,
            modifier = Modifier.fillMaxWidth().height(50.dp),
        ) {
            if (state.busy) CircularProgressIndicator(modifier = Modifier.height(20.dp))
            else Text("Entrar")
        }

        state.error?.let {
            Spacer(Modifier.height(14.dp))
            Text(it, color = androidx.compose.material3.MaterialTheme.colorScheme.error)
        }

        Spacer(Modifier.height(24.dp))
        TextButton(onClick = onOpenSettings) {
            Text("Configurar servidor (${state.baseUrl})")
        }
    }
}
