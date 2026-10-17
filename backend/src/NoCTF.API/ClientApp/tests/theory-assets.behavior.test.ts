import { expect, test } from 'bun:test'
import { computed, effectScope, reactive, ref, shallowRef, watch } from 'vue'
import * as answers from '../app/features/theory/answer-state'
import { featureController } from './support/feature-controller'

type Assets = ReturnType<typeof import('../app/features/theory/useTheoryAssets').useTheoryAssets>
type AnswerPage = ReturnType<typeof import('../app/features/theory/useTheoryAnswerPage').useTheoryAnswerPage>
const fileId = '00000000-0000-4000-8000-000000000001'
const secondFile = '00000000-0000-4000-8000-000000000002'
const source = `![image](theory-file:${fileId})`
const key = `theory-file:${fileId}`
const paper = (paperId: string) => ({ activityId: 'activity', paperId })
type ImageResult = { data?: Blob; error?: unknown }

async function assetsFixture(read: (request: { path: Record<string, string> }) => Promise<ImageResult>) {
  const cleanup: Array<() => void> = []
  const make = await featureController<Assets>('features/theory/useTheoryAssets.ts', 'useTheoryAssets', {
    shallowRef, onBeforeUnmount: (callback: () => void) => cleanup.push(callback),
    getTheoryPaperImage: read, getTheoryQuestionImage: read,
  })
  const assets = make()
  return { assets, dispose: () => cleanup.forEach(callback => callback()) }
}

test('new papers reload identical image references through their own authorized scope', async () => {
  const requested: string[] = []
  const f = await assetsFixture(async ({ path }) => { requested.push(path.paperId!); return { data: new Blob(['image']) } })
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
  const f = await assetsFixture(async ({ path }) => path.paperId === 'first'
    ? new Promise(resolve => { finish = resolve }) : { data: new Blob(['current']) })
  try {
    const old = f.assets.load([source], paper('first'))
    await f.assets.load([`![current](theory-file:${secondFile})`], paper('second'))
    const stable = f.assets.assets.value
    finish({ data: new Blob(['old']) }); await old
    expect(f.assets.assets.value).toBe(stable)
    expect(Object.keys(stable)).toEqual([`theory-file:${secondFile}`])
  }
  finally { f.dispose() }
})

test('the answer page refreshes assets after switching to an identical paper and after a download failure', async () => {
  const downloads: string[] = []
  const f = await assetsFixture(async ({ path }) => {
    downloads.push(path.paperId!)
    return downloads.length === 1 ? { error: { status: 503 } } : { data: new Blob(['image']) }
  })
  let id = 'first'
  const make = await featureController<AnswerPage>('features/theory/useTheoryAnswerPage.ts', 'useTheoryAnswerPage', {
    computed, reactive, ref, shallowRef, watch, ...answers,
    useRoute: () => ({ params: { activityId: 'activity' }, query: {} }), useRouter: () => ({}),
    useTheoryAssets: () => f.assets,
    getTheorySession: async () => ({ data: { canAnswer: true, paper: { id, state: 0, questions: [{ id: 'question', answerRevision: 'revision', body: source, options: [] }] } } }),
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
