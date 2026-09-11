-- Migration: Garantir dados de templates de catraca
-- Data: 2026-09-12
-- Descrição: 
--   Verifica e insere dados dos templates se a tabela estiver vazia

-- Se a tabela estiver vazia, inserir dados padrão
INSERT IGNORE INTO fp_turnstile_message_templates (template_id, line1, line2, active) VALUES
(0,  'ACESSO',      'LIBERADO'),
(1,  'ACESSO',      'NEGADO'),
(2,  'LIBERADA',    'ENTRE'),
(3,  'LIBERADA',    'SAIDA'),
(4,  'BLOQUEADA',   'CADEADO'),
(5,  'PASSE SEU',   'INGRESSO'),
(6,  'INGRESSO',    'INVALIDO'),
(7,  'INGRESSO',    'EXPIRADO'),
(8,  'LIMITE',      'ATINGIDO'),
(9,  'ERRO',        'REDE'),
(10, 'AGUARDE',     'VALIDACAO'),
(11, 'BEM-VINDO',   'EVENTO'),
(12, 'PROIBIDO',    'ACESSO'),
(13, 'VISITANTE',   'NAO PERMIT'),
(14, 'SETOR',       'BLOQUEADO'),
(15, 'HORARIO',     'FECHADO'),
(16, 'TENTE',       'NOVAMENTE'),
(17, 'CARTAO',      'LIDO'),
(18, 'QR CODE',     'LIDO'),
(19, 'BIOMETRIA',   'OK'),
(20, 'DUPLICADO',   'ENTRADA'),
(21, 'AGUARDE',     'OPERADOR'),
(22, 'VALIDADO',    'OPERADOR'),
(23, 'NEGADO',      'OPERADOR'),
(24, 'MODO',        'MANUTENCAO'),
(25, 'SINCRONIZAR', 'DADOS'),
(26, 'OFFLINE',     'USAR LOCAL'),
(27, 'ONLINE',      'RESTAURADO'),
(28, 'CONFIGURAR',  'CATRACA'),
(29, 'TESTE',       'CONEXAO'),
(30, 'CUSTOMIZADO', 'ID30');
