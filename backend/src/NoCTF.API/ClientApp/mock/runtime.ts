export function mockRuntimeEndpoint(index: number) {
  const hostPort = 31_001 + Math.max(0, index)
  return {
    urls: [`tcp://challenge.mock.invalid:${hostPort}`],
    access: {
      route: 'Direct', state: 'Ready', failure: null,
      endpoints: [{ containerPort: 31337, hostPort, state: 'Ready', failure: null, publicPort: null }],
    },
  }
}
