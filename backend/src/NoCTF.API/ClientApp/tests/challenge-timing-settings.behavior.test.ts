import { expect,test } from 'bun:test'
import { computed,effectScope,nextTick,onScopeDispose,ref,watch } from 'vue'
const compiled=new Bun.Transpiler({loader:'ts'}).transformSync(await Bun.file(new URL('../app/features/challenges/timing/useChallengeTimingSettings.ts',import.meta.url)).text())
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm,'').replace(/export function /g,'function ')
function fixture(){
  const mutations:any[]=[], previews:any[]=[], mounts:Array<()=>void>=[]
  const data={mode:'Ctf',challenge:{timing:{autoOpenAt:null,scoringEndsAt:'2026-10-10T10:00:00Z',submissionDeadlineAt:null,recalculationPending:false}}}
  const deps={computed,ref,watch,onScopeDispose,onMounted:(fn:()=>void)=>mounts.push(fn),message:(key:string)=>({key}),parseApiError:(x:any)=>({displayMessage:x}),toast:{success:()=>{},error:()=>{}},
    adminGetCompetitionChallenge:async()=>({data}),
    adminPreviewCompetitionChallengeTiming:async(input:any)=>{previews.push(input);return {data:{token:'preview',affectedAttempts:2,teams:[{teamId:'team',scoreBefore:500,scoreAfter:0}]}}},
    adminPatchCompetitionChallenge:async(input:any)=>{mutations.push(input);Object.assign(data.challenge.timing,input.body.timing);return {data}}}
  const factory=new Function('deps',`const {${Object.keys(deps).join(',')}}=deps;${compiled};return useChallengeTimingSettings`)(deps)
  const scope=effectScope();const state=scope.run(()=>factory({competitionId:'competition',competitionChallengeId:'challenge',canWrite:true}))!
  return {state,mutations,previews,scope,load:async()=>{mounts.forEach(fn=>fn());await nextTick();await new Promise(done=>setTimeout(done,0))},deps}
}
test('timing settings preview historical impact before any mutation and explicit clear sends null',async()=>{
  const f=fixture();try{
    await f.load()
    f.state.scoringEnd.value='2026-10-10T09:00';await nextTick();await f.state.save()
    expect(f.previews).toHaveLength(1);expect(f.mutations).toHaveLength(0);expect(f.state.confirmation.value).toBe(true)
    await f.state.apply();expect(f.mutations[0].body.timingPreviewToken).toBe('preview')
    f.state.clearScoring();await nextTick();await f.state.save();expect(f.previews.at(-1).body.timing.scoringEndsAt).toBeNull()
  }finally{f.scope.stop()}
})
test('changing the draft invalidates the review and reversed times never reach preview',async()=>{
  const f=fixture();try{
    await f.load()
    f.state.scoringEnd.value='2026-10-10T09:00';await nextTick();await f.state.save()
    f.state.scoringEnd.value='2026-10-10T10:00';await nextTick();expect(f.state.preview.value).toBeNull();expect(f.state.confirmation.value).toBe(false)
    f.state.opening.value='2026-10-10T11:00';await nextTick();await f.state.save();expect(f.previews).toHaveLength(1)
  }finally{f.scope.stop()}
})
