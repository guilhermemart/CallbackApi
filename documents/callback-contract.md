# Contrato genérico de callback

O callback deve enviar `event_type`, `event_unique_hash` e `payload`. O hash é
gerado pelo emissor e enviado como uma string opaca; o contrato não fixa um
algoritmo. O conteúdo de `payload` é preservado como JSON para aceitar dados
específicos de equipamentos diferentes.

```json
{
  "event_type": "motion_detected",
  "event_unique_hash": "sender-generated-value-0001",
  "payload": {
    "source": ["equipamento-x"],
    "source_id": "equipment-x_id",
    "created_at": "2026-08-13T12:00:00Z",
    "created_by": "user_id",
    "deleted_at": null,
    "deleted_by": null,
    "data": {
      "evento_original": "movimento",
      "dados_do_fabricante": {}
    }
  }
}
```

Campos obrigatórios definidos neste contrato: `event_type`, `event_unique_hash`,
`payload`, `payload.source`, `payload.source_id`, `payload.data`,
`payload.created_at` e `payload.created_by`. Em eventos ativos, `payload.deleted_at` e
`payload.deleted_by` podem ser nulos. Na exclusão lógica, a API preenche
`payload.deleted_at` com o horário UTC e `payload.deleted_by` com `"system_action"`
enquanto não existe autenticação. Repetir a solicitação de exclusão lógica de um
evento já excluído retorna `200 OK` com uma mensagem informativa, sem enfileirar
outra operação no banco.

Na criação, a API consulta primeiro no Redis os hashes dos dez eventos mais
recentemente aceitos para o mesmo `payload.source_id`. Uma correspondência
responde `409 Conflict` e não enfileira o evento. PostgreSQL também impõe
unicidade permanente ao par (`source_id`, `event_unique_hash`), cobrindo hashes
fora dessa janela e requisições concorrentes. O emissor deve reutilizar o mesmo
hash ao repetir o envio do mesmo evento.
