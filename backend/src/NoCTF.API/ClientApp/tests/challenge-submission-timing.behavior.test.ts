import { expect,test } from 'bun:test'
import { effectScope,reactive,ref,nextTick } from 'vue'
import { useSubmissionTiming } from '../app/features/challenges/timing/useSubmissionTiming'

test('the actual feature timer distinguishes judgement and deadline while practice ignores close',()=>{
  const scope=effectScope(),practice=ref(false)
  const timing=reactive({scoringEndsAt:'2026-10-10T10:00:00Z',submissionDeadlineAt:'2026-10-10T11:00:00Z'})
  try{
    const state=scope.run(()=>useSubmissionTiming(()=>timing,()=>practice.value,()=>Date.parse('2026-10-10T11:00:00Z')))!
    expect(state.judgementOnly.value).toBe(true);expect(state.submissionsClosed.value).toBe(true)
    practice.value=true;expect(state.submissionsClosed.value).toBe(false)
    practice.value=false;timing.submissionDeadlineAt='2026-10-10T12:00:00Z';expect(state.submissionsClosed.value).toBe(false)
    timing.scoringEndsAt='2026-10-10T12:00:00Z';expect(state.judgementOnly.value).toBe(false)
  }finally{scope.stop()}
})

test('server time corrects a slow browser clock and refresh applies the new server instant',async()=>{
  const scope=effectScope()
  const timing=reactive({serverTime:'2026-10-10T11:00:00Z',scoringEndsAt:'2026-10-10T10:00:00Z',submissionDeadlineAt:'2026-10-10T11:00:00Z'})
  try{
    const state=scope.run(()=>useSubmissionTiming(()=>timing,()=>false,()=>Date.parse('2026-10-10T09:00:00Z')))!
    expect(state.submissionsClosed.value).toBe(true)
    timing.serverTime='2026-10-10T09:00:00Z';await nextTick()
    expect(state.submissionsClosed.value).toBe(false);expect(state.judgementOnly.value).toBe(false)
  }finally{scope.stop()}
})


test('challenge route catalog loads timing feedback in both supported languages',async()=>{
  const {ensureLocaleDomains,prepareLocale,setLocale,translate}=await import('../app/utils/i18n')
  try{await ensureLocaleDomains(['challenges']);await prepareLocale('en');setLocale('en');expect(translate('challengeTiming.title')).not.toBe('challengeTiming.title');setLocale('zh-CN');expect(translate('challengeTiming.title')).toBe('开放与计分时段')}
  finally{setLocale('zh-CN')}
})
