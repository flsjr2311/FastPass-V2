-- Migration: Repopular templates de catraca
-- Data: 2026-09-12
-- Descrição: 
--   Garante que a tabela fp_turnstile_message_templates contém todos os 31 templates

DELETE FROM fp_turnstile_message_templates WHERE template_id IS NOT NULL;

INSERT INTO fp_turnstile_message_templates VALUES
(0,  'ACESSO',      'LIBERADO',  1, NOW(), NOW()),
(1,  'ACESSO',      'NEGADO',    1, NOW(), NOW()),
(2,  'LIBERADA',    'ENTRE',     1, NOW(), NOW()),
(3,  'LIBERADA',    'SAIDA',     1, NOW(), NOW()),
(4,  'BLOQUEADA',   'CADEADO',   1, NOW(), NOW()),
(5,  'PASSE SEU',   'INGRESSO',  1, NOW(), NOW()),
(6,  'INGRESSO',    'INVALIDO',  1, NOW(), NOW()),
(7,  'INGRESSO',    'EXPIRADO',  1, NOW(), NOW()),
(8,  'LIMITE',      'ATINGIDO',  1, NOW(), NOW()),
(9,  'ERRO',        'REDE',      1, NOW(), NOW()),
(10, 'AGUARDE',     'VALIDACAO', 1, NOW(), NOW()),
(11, 'BEM-VINDO',   'EVENTO',    1, NOW(), NOW()),
(12, 'PROIBIDO',    'ACESSO',    1, NOW(), NOW()),
(13, 'VISITANTE',   'NAO PERMIT',1, NOW(), NOW()),
(14, 'SETOR',       'BLOQUEADO', 1, NOW(), NOW()),
(15, 'HORARIO',     'FECHADO',   1, NOW(), NOW()),
(16, 'TENTE',       'NOVAMENTE', 1, NOW(), NOW()),
(17, 'CARTAO',      'LIDO',      1, NOW(), NOW()),
(18, 'QR CODE',     'LIDO',      1, NOW(), NOW()),
(19, 'BIOMETRIA',   'OK',        1, NOW(), NOW()),
(20, 'DUPLICADO',   'ENTRADA',   1, NOW(), NOW()),
(21, 'AGUARDE',     'OPERADOR',  1, NOW(), NOW()),
(22, 'VALIDADO',    'OPERADOR',  1, NOW(), NOW()),
(23, 'NEGADO',      'OPERADOR',  1, NOW(), NOW()),
(24, 'MODO',        'MANUTENCAO',1, NOW(), NOW()),
(25, 'SINCRONIZAR', 'DADOS',     1, NOW(), NOW()),
(26, 'OFFLINE',     'USAR LOCAL',1, NOW(), NOW()),
(27, 'ONLINE',      'RESTAURADO',1, NOW(), NOW()),
(28, 'CONFIGURAR',  'CATRACA',   1, NOW(), NOW()),
(29, 'TESTE',       'CONEXAO',   1, NOW(), NOW()),
(30, 'CUSTOMIZADO', 'ID30',      1, NOW(), NOW());
