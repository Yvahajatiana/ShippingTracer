import { parseAppConfig, safeOrigin } from './app-config';

describe('safeOrigin', () => {
  it('normalizes https origins', () => {
    expect(safeOrigin('https://api.parcelflow.example/')).toBe('https://api.parcelflow.example');
  });

  it('allows plain http only on loopback', () => {
    expect(safeOrigin('http://localhost:5100')).toBe('http://localhost:5100');
    expect(() => safeOrigin('http://api.parcelflow.example')).toThrow();
  });

  it('rejects credentials and other protocols', () => {
    expect(() => safeOrigin('https://user:secret@api.example')).toThrow();
    expect(() => safeOrigin('javascript:alert(1)')).toThrow();
  });
});

describe('parseAppConfig', () => {
  it('accepts a mock configuration without Keycloak settings', () => {
    const config = parseAppConfig({ authMode: 'mock', mockApi: true });
    expect(config).toEqual({
      apiBaseUrl: '',
      authMode: 'mock',
      mockApi: true,
      keycloak: { url: '', realm: '', clientId: '' },
    });
  });

  it('defaults to Keycloak and requires its settings', () => {
    expect(() => parseAppConfig({})).toThrow(/Keycloak/);
    const config = parseAppConfig({
      keycloak: { url: 'https://auth.example', realm: 'parcelflow', clientId: 'parcelflow-admin' },
    });
    expect(config.authMode).toBe('keycloak');
    expect(config.mockApi).toBe(false);
  });

  it('rejects unsafe API origins', () => {
    expect(() => parseAppConfig({ authMode: 'mock', apiBaseUrl: 'http://evil.example' })).toThrow();
  });
});
