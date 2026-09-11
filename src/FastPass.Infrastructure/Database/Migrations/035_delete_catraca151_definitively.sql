-- Deletar Catraca151 definitivamente
-- A catraca foi renomeada para Catraca156, então a entrada antiga não é mais necessária

-- Primeiro deletar tentativas de acesso relacionadas
DELETE FROM fp_access_attempts WHERE device_id IN (
  SELECT id FROM fp_devices WHERE identifier = 'Catraca151'
);

-- Depois deletar presença registrada
DELETE FROM fp_turnstile_presence WHERE device_id = 'Catraca151';

-- Finalmente deletar o device
DELETE FROM fp_devices WHERE identifier = 'Catraca151';

-- Catraca151 foi removida do sistema. Agora apenas Catraca156 (Pista) e Catraca157 (Camarote) existem.
