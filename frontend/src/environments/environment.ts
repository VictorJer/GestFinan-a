// Usa a origem loopback da API local; este endereço público não contém segredo.
export const environment: { readonly apiBaseUrl: string | null } = {
  apiBaseUrl: 'http://127.0.0.1:5080',
};
