# Teste de carga de validação de acesso

Script para simular tráfego de portaria (validações concorrentes) e observar como o
Dashboard e os Relatórios reagem em tempo real durante um evento simulado.

## Uso rápido

```powershell
.\Invoke-AccessValidationLoadTest.ps1 -EventId "SEU-EVENT-ID-AQUI"
```

Padrão: 12 minutos, 6 validações simultâneas por onda (~1 onda/segundo), 10% de taxa de erro.

## Parâmetros principais

| Parâmetro | Padrão | Descrição |
|---|---|---|
| `-EventId` | *(obrigatório)* | GUID do evento a testar |
| `-ApiBaseUrl` | `http://127.0.0.1:5104` | URL base da API |
| `-DurationMinutes` | `12` | Duração total do teste |
| `-Concurrency` | `6` | Validações simultâneas por onda |
| `-ErrorRatePercent` | `10` | % de tentativas que devem falhar de propósito |
| `-WaveDelayMs` | `1000` | Pausa entre ondas (controla req/s) |

## Cenários de erro simulados

Dentro da fração de erro (`ErrorRatePercent`), o script varia entre:
- **Ingresso já usado** — reenvia um código já aprovado nesta mesma execução (~40% dos erros)
- **Ingresso cancelado** — usa um ticket com status `cancelled` já existente no evento (~25%)
- **Ingresso inexistente / outro evento** — gera um código aleatório inválido (~20%)
- **Portaria sem autorização** — usa uma combinação portaria×setor fora da matriz cadastrada (~15%)

## Pré-requisitos

- API FastPass rodando e acessível
- O evento deve ter pelo menos 1 ticket ativo
- O evento deve ter pelo menos 1 associação ativa portaria×setor (direção Entry) em
  **Portarias e Setores**

## Atenção

O script consome tickets ativos reais (marca como usados) do evento informado.
**Não rode contra um evento em operação real** — use um evento de teste/homologação.

## Acompanhando o resultado

Enquanto o script roda, abra no navegador:
- **Dashboard** — cards de total/validados/faltam validar/dentro agora, cobertura por setor, feed dos últimos acessos (atualiza a cada 30s)
- **Relatórios** — desempenho por portaria, cobertura por setor, motivos de rejeição, últimas tentativas

Ao final, o script imprime um resumo com total de validações, taxa de aprovação/rejeição
e a contagem de cada motivo de rejeição.
