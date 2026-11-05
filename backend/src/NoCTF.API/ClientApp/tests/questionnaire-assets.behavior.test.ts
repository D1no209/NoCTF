import { expect, test } from 'bun:test'
import { computed, effectScope, reactive, ref, shallowRef, watch } from 'vue'
import * as answers from '../app/features/questionnaires/answer-state'
import { featureController } from './support/feature-controller'

type Assets = ReturnType<typeof import('../app/features/questionnaires/useQuestionnaireAssets').useQuestionnaireAssets>
type AnswerPage = ReturnType<typeof import('../app/features/questionnaires/useQuestionnaireAnswerPage').useQuestionnaireAnswerPage>
const fileId = '00000000-0000-4000-8000-000000000001'
const secondFile = '00000000-0000-4000-8000-000000000002'
const source = `![image](questionnaire-file:${fileId})`
const key = `questionnaire-file:${fileId}`
const paper = (sessionId: string) => ({ activityId: 'activity', sessionId })
type ImageResult = { data?: Blob; error?: unknown }

async function assetsFixture(read: (request: { path: Record<string, string> }) => Promise<ImageResult>) {
  const cleanup: Array<() => void> = []
  const make = await featureController<Assets>('features/questionnaires/useQuestionnaireAssets.ts', 'useQuestionnaireAssets', {
    shallowRef, onBeforeUnmount: (callback: () => void) => cleanup.push(callback),
    getQuestionnaireSessionImage: read, adminChallengeBankGetQuestionnaireImage: read,
  })
  const assets = make()
  return { assets, dispose: () => cleanup.forEach(callback => callback()) }
}

test('new papers reload identical image references through their own authorized scope', async () => {
  const requested: string[] = []
  const f = await assetsFixture(async ({ path }) => { requested.push(path.sessionId!); return { data: new Blob(['image']) } })
  try {
    await f.assets.load([source], paper('first'))
    const oldUrl = f.assets.assets.value[key]!
    await f.assets.load([source], paper('second'))
    expect(requested).toEqual(['first', 'second'])
    expect(f.assets.assets.value[key]).not.toBe(oldUrl)
    await expect(fetch(oldUrl)).rejects.toThrow()
  }
  finally { f.dispose() }
})

test('unchanged loaded images keep the same object and do not request another download', async () => {
  let requests = 0
  const f = await assetsFixture(async () => { requests++; return { data: new Blob(['image']) } })
  try {
    await f.assets.load([source], paper('first'))
    const stable = f.assets.assets.value
    await f.assets.load([source], paper('first'))
    expect(f.assets.assets.value).toBe(stable)
    expect(requests).toBe(1)
  }
  finally { f.dispose() }
})

test('simultaneous refreshes share the same pending image load', async () => {
  let finish!: (value: ImageResult) => void, requests = 0
  const f = await assetsFixture(async () => { requests++; return new Promise(resolve => { finish = resolve }) })
  try {
    const first = f.assets.load([source], paper('first'))
    const next = f.assets.load([source], paper('first'))
    expect(requests).toBe(1)
    finish({ data: new Blob(['image']) })
    await Promise.all([first, next])
    expect(Object.keys(f.assets.assets.value)).toEqual([key])
  }
  finally { f.dispose() }
})

test('failed image downloads can be retried without changing the question text', async () => {
  let requests = 0
  const f = await assetsFixture(async () => ++requests === 1 ? { error: { status: 503 } } : { data: new Blob(['image']) })
  try {
    await f.assets.load([source], paper('first'))
    expect(f.assets.assets.value).toEqual({})
    await f.assets.load([source], paper('first'))
    expect(f.assets.assets.value[key]?.startsWith('blob:')).toBe(true)
    expect(requests).toBe(2)
  }
  finally { f.dispose() }
})

test('a late response from the previous paper cannot replace current images', async () => {
  let finish!: (value: ImageResult) => void
  const f = await assetsFixture(async ({ path }) => path.sessionId === 'first'
    ? new Promise(resolve => { finish = resolve }) : { data: new Blob(['current']) })
  try {
    const old = f.assets.load([source], paper('first'))
    await f.assets.load([`![current](questionnaire-file:${secondFile})`], paper('second'))
    const stable = f.assets.assets.value
    finish({ data: new Blob(['old']) }); await old
    expect(f.assets.assets.value).toBe(stable)
    expect(Object.keys(stable)).toEqual([`questionnaire-file:${secondFile}`])
  }
  finally { f.dispose() }
})

test('the answer page refreshes assets after switching to an identical paper and after a download failure', async () => {
  const downloads: string[] = []
  const f = await assetsFixture(async ({ path }) => {
    downloads.push(path.sessionId!)
    return downloads.length === 1 ? { error: { status: 503 } } : { data: new Blob(['image']) }
  })
  let id = 'first'
  const make = await featureController<AnswerPage>('features/questionnaires/useQuestionnaireAnswerPage.ts', 'useQuestionnaireAnswerPage', {
    computed, reactive, ref, shallowRef, watch, ...answers,
    useRoute: () => ({ params: { activityId: 'activity' }, query: {} }), useRouter: () => ({}),
    useQuestionnaireAssets: () => f.assets,
    getQuestionnaireActivitySession: async () => ({ data: { canAnswer: true, session: { id, state: 0, questions: [{ id: 'question', answerRevision: 'revision', body: source, options: [] }] } } }),
    onBeforeRouteLeave: () => {}, onBeforeRouteUpdate: () => {}, onMounted: () => {}, onBeforeUnmount: () => {},
    message: (key: string) => key, t: (key: string) => key, questionKindLabel: () => '', scoringLabel: () => '',
  })
  const scope = effectScope(), state = scope.run(make)!
  try {
    await state.load(); await new Promise(resolve => setTimeout(resolve, 0))
    expect(f.assets.assets.value).toEqual({})
    await state.load(); await new Promise(resolve => setTimeout(resolve, 0))
    expect(f.assets.assets.value[key]).toBeDefined()
    id = 'second'
    await state.load(); await new Promise(resolve => setTimeout(resolve, 0))
    expect(downloads).toEqual(['first', 'first', 'second'])
    expect(f.assets.assets.value[key]).toBeDefined()
  }
  finally { scope.stop(); f.dispose() }
})
