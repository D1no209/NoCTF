import { computed,onScopeDispose,ref,watch } from 'vue'
import type { NoCtfapiEndpointsChallengesCompetitionChallengeTimingResponse as Timing } from '~/api'

export function useSubmissionTiming(timing:()=>Timing|null|undefined,practice:()=>boolean=()=>false,
  clock:()=>number=Date.now) {
  const now=ref(clock()),offset=ref(0),timer=setInterval(()=>{now.value=clock()+offset.value},1000)
  watch(()=>timing()?.serverTime,value=>{offset.value=value?Date.parse(value)-clock():0;now.value=clock()+offset.value},{immediate:true})
  onScopeDispose(()=>clearInterval(timer))
  const submissionsClosed=computed(()=>!practice()&&!!timing()?.submissionDeadlineAt&&now.value>=Date.parse(timing()!.submissionDeadlineAt!))
  const judgementOnly=computed(()=>practice()||!!timing()?.scoringEndsAt&&now.value>=Date.parse(timing()!.scoringEndsAt!))
  return {submissionsClosed,judgementOnly}
}
