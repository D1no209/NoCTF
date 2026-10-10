import { computed, onMounted, onScopeDispose, ref, watch } from 'vue'
import { adminGetCompetitionChallenge, adminPatchCompetitionChallenge, adminPreviewCompetitionChallengeTiming } from '~/api'
import type { NoCtfApplicationChallengesTimingChallengeTimingPreview as Preview,
  NoCtfapiEndpointsChallengesCompetitionChallengeTimingResponse as Timing } from '~/api'
import { message, type UiMessage } from '~/utils/i18n'
import { toast } from '~/utils/message-toast'

export function useChallengeTimingSettings(props: Readonly<{ competitionId: string; competitionChallengeId: string; canWrite: boolean }>) {
  const loading = ref(true), pending = ref(false), error = ref<UiMessage|null>(null), supported = ref(true)
  const opening = ref(''), scoringEnd = ref(''), submissionEnd = ref(''), saved = ref(''), current = ref<Timing|null>(null)
  const preview = ref<Preview|null>(null), confirmation = ref(false)
  const zone = Intl.DateTimeFormat().resolvedOptions().timeZone
  let disposed = false, generation = 0, timer:ReturnType<typeof setTimeout>|undefined
  const candidate = computed(() => ({ autoOpenAt: iso(opening.value), scoringEndsAt: iso(scoringEnd.value), submissionDeadlineAt: iso(submissionEnd.value) }))
  const valid = computed(() => [opening.value,scoringEnd.value,submissionEnd.value].every(value=>!value||Number.isFinite(Date.parse(value)))
    && (!opening.value||!scoringEnd.value||Date.parse(opening.value)<=Date.parse(scoringEnd.value))
    && (!opening.value||!submissionEnd.value||Date.parse(opening.value)<=Date.parse(submissionEnd.value))
    && (!scoringEnd.value||!submissionEnd.value||Date.parse(scoringEnd.value)<=Date.parse(submissionEnd.value)))
  const dirty = computed(()=>JSON.stringify(candidate.value)!==saved.value)
  function iso(value:string){return value&&Number.isFinite(Date.parse(value))?new Date(value).toISOString():null}
  function local(value:string|null|undefined){if(!value)return '';const date=new Date(value);return new Date(date.getTime()-date.getTimezoneOffset()*60_000).toISOString().slice(0,23)}
  function fill(timing:Timing|null|undefined){current.value=timing??null;opening.value=local(timing?.autoOpenAt);scoringEnd.value=local(timing?.scoringEndsAt);submissionEnd.value=local(timing?.submissionDeadlineAt);saved.value=JSON.stringify(candidate.value)}
  async function load(initial=true){
    const id=++generation
    try {
      const result=await adminGetCompetitionChallenge({path:{competitionId:props.competitionId,competitionChallengeId:props.competitionChallengeId},query:{includeDeleted:false}})
      if(disposed||id!==generation)return
      if(result.error||!result.data)throw result.error
      supported.value=result.data.mode!=='LiveSolo'
      if(initial||!dirty.value)fill(result.data.challenge?.timing)
      else current.value=result.data.challenge?.timing??null
      if(current.value?.recalculationPending)timer=setTimeout(()=>{void load(false)},2000)
    }catch(cause){if(!disposed&&id===generation)error.value=parseApiError(cause,message('challengeTiming.loadFailed')).displayMessage}
    finally{if(!disposed&&id===generation)loading.value=false}
  }
  async function save(){
    if(pending.value||!valid.value||!dirty.value||!props.canWrite)return
    pending.value=true;error.value=null
    try {
      const result=await adminPreviewCompetitionChallengeTiming({path:{competitionId:props.competitionId,competitionChallengeId:props.competitionChallengeId},body:{timing:candidate.value}})
      if(result.error||!result.data?.token)throw result.error
      if(disposed)return
      preview.value=result.data
      if((result.data.affectedAttempts??0)>0||(result.data.teams?.length??0)>0)confirmation.value=true
      else await apply()
    }catch(cause){toast.error(parseApiError(cause,message('challengeTiming.previewFailed')).displayMessage)}
    finally{pending.value=false}
  }
  async function apply(){
    if(!preview.value?.token||!valid.value)return
    pending.value=true
    try {
      const result=await adminPatchCompetitionChallenge({path:{competitionId:props.competitionId,competitionChallengeId:props.competitionChallengeId},body:{timing:candidate.value,timingPreviewToken:preview.value.token}})
      if(result.error||!result.data)throw result.error
      if(disposed)return
      confirmation.value=false;preview.value=null;fill(result.data.challenge?.timing)
      toast.success(message('challengeTiming.saved'));if(current.value?.recalculationPending)timer=setTimeout(()=>{void load(false)},2000)
    }catch(cause){confirmation.value=false;preview.value=null;toast.error(parseApiError(cause,message('challengeTiming.saveFailed')).displayMessage)}
    finally{pending.value=false}
  }
  function setConfirmation(open:boolean){if(!pending.value)confirmation.value=open}
  function clearOpening(){opening.value=''}function clearScoring(){scoringEnd.value=''}function clearSubmission(){submissionEnd.value=''}
  watch([opening,scoringEnd,submissionEnd],()=>{preview.value=null;confirmation.value=false})
  onMounted(()=>{void load()});onScopeDispose(()=>{disposed=true;generation++;if(timer)clearTimeout(timer)})
  return {loading,pending,error,supported,opening,scoringEnd,submissionEnd,current,zone,valid,dirty,preview,confirmation,
    canWrite:computed(()=>props.canWrite),save,apply,setConfirmation,clearOpening,clearScoring,clearSubmission}
}
export type ChallengeTimingSettingsState=import('vue').ShallowUnwrapRef<ReturnType<typeof useChallengeTimingSettings>>
