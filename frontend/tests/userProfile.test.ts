import { afterAll, beforeEach, describe, expect, test } from 'bun:test'
import { client } from '../src/api/generated/client.gen'
import { authApi } from '../src/api/noctf'
import {
  avatarDrawMetrics,
  clampAvatarCropState,
  moveAvatarCrop,
  zoomAvatarCropAtPoint,
} from '../src/components/home/avatarCrop'

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
  isEmailPublic: false,
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
    await expect(authApi.updateProfile({
      description: 'Profile',
      isEmailPublic: true,
    })).resolves.toEqual(profile)

    expect(requests).toHaveLength(1)
    expect(requests[0]!.method).toBe('PUT')
    expect(new URL(requests[0]!.url).pathname).toBe('/api/v1/auth/me/profile')
    expect(await requests[0]!.clone().json()).toEqual({
      description: 'Profile',
      isEmailPublic: true,
    })
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
  test('covers the viewport and clamps offsets to the visible image', () => {
    expect(
      avatarDrawMetrics(640, 320, 320, {
        zoom: 1,
        offsetX: 400,
        offsetY: 40,
        rotation: 0,
      }),
    ).toEqual({ scale: 1, offsetX: 160, offsetY: 0, maximumX: 160, maximumY: 0 })
  })

  test('uses rotated dimensions for a quarter turn', () => {
    expect(
      avatarDrawMetrics(640, 320, 320, {
        zoom: 1,
        offsetX: 0,
        offsetY: 400,
        rotation: 90,
      }),
    ).toEqual({ scale: 1, offsetX: 0, offsetY: 160, maximumX: 0, maximumY: 160 })
  })

  test('reclamps both axes after rotation without exposing empty pixels', () => {
    expect(
      clampAvatarCropState(640, 320, 320, {
        zoom: 1,
        offsetX: 150,
        offsetY: 150,
        rotation: 90,
      }),
    ).toEqual({ zoom: 1, offsetX: 0, offsetY: 150, rotation: 90 })
  })

  test('wheel zoom preserves the image point below the pointer before boundary clamp', () => {
    expect(
      zoomAvatarCropAtPoint(640, 640, 320, {
        zoom: 1,
        offsetX: 0,
        offsetY: 0,
        rotation: 0,
      }, 2, 60, -40),
    ).toEqual({ zoom: 2, offsetX: -60, offsetY: 40, rotation: 0 })
  })

  test('pointer movement uses viewport pixels and remains boundary-clamped', () => {
    expect(
      moveAvatarCrop(640, 320, 320, {
        zoom: 1,
        offsetX: 120,
        offsetY: 0,
        rotation: 0,
      }, 100, 80),
    ).toEqual({ zoom: 1, offsetX: 160, offsetY: 0, rotation: 0 })
  })
})

describe('profile workspace', () => {
  test('keeps editing on /profile and exposes it through the navigation avatar', async () => {
    const home = await Bun.file(
      new URL('../src/components/home/HomeWorkspace.vue', import.meta.url),
    ).text()
    const navigation = await Bun.file(
      new URL('../src/components/layout/NavBar.vue', import.meta.url),
    ).text()
    const workspace = await Bun.file(
      new URL('../src/components/profile/ProfileWorkspace.vue', import.meta.url),
    ).text()
    const editor = await Bun.file(
      new URL('../src/components/home/AvatarEditorDialog.vue', import.meta.url),
    ).text()
    const router = await Bun.file(new URL('../src/router/index.ts', import.meta.url)).text()

    expect(router).toContain(`path: '/profile'`)
    expect(home).not.toContain(`name: 'profile'`)
    expect(home).not.toContain('currentUser')
    expect(navigation).toContain(`:to="{ name: 'profile' }"`)
    expect(navigation).toContain('v-if="avatarUrl"')
    expect(home).not.toContain('type="file"')
    expect(home).not.toContain('<Textarea')
    expect(editor).toContain('@wheel.prevent="handleWheel"')
    expect(editor).toContain('setPointerCapture(event.pointerId)')
    expect(editor).toContain('absolute inset-0 box-border rounded-full')
    expect(editor).not.toContain('absolute inset-4 rounded-full')
    expect(editor).not.toContain('type="range"')
    expect(workspace).toContain('changePasswordMutation')
  })
})
