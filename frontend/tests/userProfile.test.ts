import { afterAll, beforeEach, describe, expect, test } from 'bun:test'
import { client } from '../src/api/generated/client.gen'
import { authApi } from '../src/api/noctf'
import { avatarDrawMetrics } from '../src/components/home/avatarCrop'

const apiBaseUrl = 'https://api.noctf.test'
const originalClientConfig = client.getConfig()
const requests: Request[] = []
const profile = {
  userId: '11111111-1111-1111-1111-111111111111',
  userName: 'Player',
  email: 'player@example.test',
  role: 0,
  kind: 0,
  emailVerified: true,
  description: 'Profile',
  avatarUrl: '/api/v1/users/11111111-1111-1111-1111-111111111111/avatar?revision=one',
} as const

const contractFetch: typeof fetch = async (input, init) => {
  const normalizedInput
    = typeof input === 'string' && input.startsWith('/') ? new URL(input, apiBaseUrl) : input
  const request = new Request(normalizedInput, init)
  requests.push(request)
  return Response.json(profile)
}

beforeEach(() => {
  requests.length = 0
  client.setConfig({ baseUrl: apiBaseUrl, fetch: contractFetch })
})

afterAll(() => client.setConfig(originalClientConfig))

describe('user profile contract', () => {
  test('updates the description through the generated endpoint', async () => {
    await expect(authApi.updateProfile('Profile')).resolves.toEqual(profile)

    expect(requests).toHaveLength(1)
    expect(requests[0]!.method).toBe('PUT')
    expect(new URL(requests[0]!.url).pathname).toBe('/api/v1/auth/me/profile')
    expect(await requests[0]!.clone().json()).toEqual({ description: 'Profile' })
  })

  test('uploads only the cropped file through generated multipart binding', async () => {
    const file = new File(['cropped'], 'avatar.webp', { type: 'image/webp' })
    await expect(authApi.uploadAvatar(file)).resolves.toEqual(profile)

    expect(requests).toHaveLength(1)
    expect(requests[0]!.method).toBe('POST')
    expect(new URL(requests[0]!.url).pathname).toBe('/api/v1/auth/me/avatar')
    const body = await requests[0]!.clone().formData()
    expect(body.get('file')).toBeInstanceOf(File)
    expect((body.get('file') as File).name).toBe('avatar.webp')
  })
})

describe('avatar crop geometry', () => {
  test('covers the viewport and clamps normalized offsets', () => {
    expect(
      avatarDrawMetrics(640, 320, 320, {
        zoom: 1,
        positionX: 0.5,
        positionY: 2,
        rotation: 0,
      }),
    ).toEqual({ scale: 1, offsetX: 80, offsetY: 0 })
  })

  test('uses rotated dimensions for a quarter turn', () => {
    expect(
      avatarDrawMetrics(640, 320, 320, {
        zoom: 1,
        positionX: 0,
        positionY: 1,
        rotation: 90,
      }),
    ).toEqual({ scale: 1, offsetX: 0, offsetY: 160 })
  })
})
