package com.fastpass.validator.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
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
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.fastpass.validator.ui.FeedItem
import com.fastpass.validator.ui.UiState
import com.fastpass.validator.ui.theme.ApprovedGreen
import com.fastpass.validator.ui.theme.RejectedRed
import com.fastpass.validator.ui.scanner.CameraScanner
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

@Composable
fun ScannerScreen(
    state: UiState,
    hasCameraPermission: Boolean,
    onRequestCamera: () -> Unit,
    onBack: () -> Unit,
    onCode: (String) -> Unit,
) {
    var manual by remember { mutableStateOf("") }
    val result = state.lastResult

    Column(modifier = Modifier.fillMaxSize()) {
        // Cabeçalho
        Row(
            modifier = Modifier.fillMaxWidth().background(MaterialTheme.colorScheme.secondary).padding(8.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            IconButton(onClick = onBack) {
                Icon(Icons.Filled.ArrowBack, contentDescription = "Voltar", tint = Color.White)
            }
            Column {
                Text(state.selectedEvent?.name ?: "-", color = Color.White, fontWeight = FontWeight.Bold)
                Text(
                    "Portaria: ${state.selectedGate?.name ?: "-"}",
                    color = Color.White,
                    fontSize = 12.sp,
                )
            }
        }

        // Área principal: câmera OU resultado grande
        Box(modifier = Modifier.fillMaxWidth().weight(1f)) {
            if (result != null) {
                ResultOverlay(state)
            } else if (hasCameraPermission) {
                CameraScanner(modifier = Modifier.fillMaxSize(), onBarcode = onCode)
                Text(
                    "Aponte para o QR do ingresso",
                    color = Color.White,
                    modifier = Modifier.align(Alignment.BottomCenter).padding(16.dp),
                )
            } else {
                Column(
                    modifier = Modifier.fillMaxSize().padding(24.dp),
                    verticalArrangement = Arrangement.Center,
                    horizontalAlignment = Alignment.CenterHorizontally,
                ) {
                    Text("Câmera não autorizada.", fontWeight = FontWeight.Bold)
                    Text(
                        "Você pode liberar a câmera, usar um leitor USB-C ou digitar o código abaixo.",
                        fontSize = 12.sp,
                    )
                    Spacer(Modifier.height(12.dp))
                    Button(onClick = onRequestCamera) { Text("Permitir câmera") }
                }
            }

            if (state.validating) {
                Text(
                    "Validando...",
                    color = Color.White,
                    modifier = Modifier.align(Alignment.Center)
                        .background(Color(0x99000000)).padding(12.dp),
                )
            }
        }

        // Entrada manual / leitor USB-C (o leitor HID digita aqui e envia Enter)
        Column(modifier = Modifier.fillMaxWidth().padding(12.dp)) {
            OutlinedTextField(
                value = manual,
                onValueChange = { manual = it },
                label = { Text("Código (manual ou leitor USB-C)") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
            )
            Spacer(Modifier.height(6.dp))
            Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Button(
                    onClick = {
                        if (manual.isNotBlank()) {
                            onCode(manual)
                            manual = ""
                        }
                    },
                    modifier = Modifier.weight(1f),
                ) { Text("Validar") }
                if (result != null) {
                    OutlinedButton(onClick = { manual = "" }, modifier = Modifier.weight(1f)) {
                        Text("Próximo")
                    }
                }
            }

            if (state.feed.isNotEmpty()) {
                Spacer(Modifier.height(10.dp))
                Text("Últimas validações", fontWeight = FontWeight.Bold, fontSize = 12.sp)
                LazyColumn(modifier = Modifier.fillMaxWidth().height(120.dp)) {
                    items(state.feed) { FeedRow(it) }
                }
            }
        }
    }
}

@Composable
private fun ResultOverlay(state: UiState) {
    val result = state.lastResult ?: return
    val color = if (result.approved) ApprovedGreen else RejectedRed
    Column(
        modifier = Modifier.fillMaxSize().background(color).padding(24.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text(if (result.approved) "✓" else "✕", color = Color.White, fontSize = 90.sp, fontWeight = FontWeight.Bold)
        Text(
            if (result.approved) "LIBERADO" else "NEGADO",
            color = Color.White,
            fontSize = 34.sp,
            fontWeight = FontWeight.ExtraBold,
        )
        Spacer(Modifier.height(10.dp))
        val text = result.message ?: result.reason ?: if (result.approved) "Acesso autorizado" else "Acesso negado"
        Text(text, color = Color.White, fontSize = 16.sp)
        if (result.idempotentReplay) {
            Spacer(Modifier.height(6.dp))
            Text("(releitura — já processado)", color = Color.White, fontSize = 12.sp)
        }
        result.peopleInside?.let {
            Spacer(Modifier.height(6.dp))
            Text("Pessoas dentro: $it", color = Color.White, fontSize = 13.sp)
        }
    }
}

private val timeFmt = SimpleDateFormat("HH:mm:ss", Locale.getDefault())

@Composable
private fun FeedRow(item: FeedItem) {
    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp),
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(
            (if (item.approved) "✓ " else "✕ ") + item.code,
            color = if (item.approved) ApprovedGreen else RejectedRed,
            fontSize = 12.sp,
            fontWeight = FontWeight.SemiBold,
        )
        Text("${item.label} · ${timeFmt.format(Date(item.timeMillis))}", fontSize = 11.sp)
    }
}
