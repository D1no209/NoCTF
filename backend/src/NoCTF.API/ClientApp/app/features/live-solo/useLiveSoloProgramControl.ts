import { computed, inject, onMounted, onScopeDispose, ref } from 'vue'
import { getLiveSoloProgramHealth, listLiveSoloProgramDecisions, recoverLiveSoloProgram } from '~/api'
import type { NoCtfapiEndpointsLiveSoloLiveSoloProgramHealthResponse as Health, NoCtfapiEndpointsLiveSoloLiveSoloProgramDecisionResponse as Decision,
  NoCtfDomainLiveSoloLiveSoloProgramAction as Action } from '~/api'
import { competitionContextKey } from '~/utils/labels'
import { canManageLiveSolo } from './settings-draft'
import { message, type UiMessage } from '~/utils/i18n'
import type { MessageKey } from '~/locales/en'
import { parseLiveSoloError } from './live-solo-errors'
import { availableProgramRecovery } from './program-recovery-policy'
export function useLiveSoloProgramControl() {
  const route=useRoute(), context=inject(competitionContextKey)
  const path={competitionId:route.params.id as string,matchId:route.params.matchId as string}
  const writable=computed(()=>canManageLiveSolo(context?.competition.value?.administrationRole))
  const program=ref<Health|null>(null), history=ref<Decision[]>([]), error=ref<UiMessage|null>(null), busy=ref(false), reason=ref('')
  const decision=ref<{action:Action;program:Health}|null>(null)
  const keys={ReconcileExport:'liveSolo.programControl.reconcile',RetryImport:'liveSolo.programControl.retry',Rotate:'liveSolo.programControl.rotate'} satisfies Record<Action,MessageKey>
  const actions=computed(()=>availableProgramRecovery(program.value,writable.value).map(action=>({action,key:keys[action]})))
  const historyRows=computed(()=>history.value.map(entry=>({entry,key:entry.action?keys[entry.action]:'liveSolo.programControl.title' as MessageKey})))
  const actionKey=computed(()=>decision.value?keys[decision.value.action]:'liveSolo.programControl.title')
  const stateKeys={Pending:'liveSolo.programControl.pending',Starting:'liveSolo.programControl.starting',Active:'liveSolo.programControl.active',Stopping:'liveSolo.programControl.stopping',Completed:'liveSolo.programControl.completed',Failed:'liveSolo.programControl.failed',RequiresReview:'liveSolo.programControl.review'} satisfies Record<import('~/api').NoCtfDomainLiveSoloLiveSoloCaptureState,MessageKey>
  const stateKey=computed(()=>program.value?.state?stateKeys[program.value.state]:'liveSolo.programControl.pending')
  let disposed=false, timer:ReturnType<typeof setTimeout>|undefined, generation=0
  async function load(clearFeedback=true) {
    if(disposed||busy.value||decision.value)return
    const id=++generation
    try {
      const [health,records]=await Promise.all([getLiveSoloProgramHealth({path}),listLiveSoloProgramDecisions({path})])
      if(disposed||id!==generation)return
      if(health.response?.status===404){program.value=null;history.value=records.data??[];return}
      if(health.error||!health.data||records.error)throw parseLiveSoloError(health.error??records.error,message('liveSolo.error.load'))
      program.value=health.data;history.value=records.data??[];if(clearFeedback)error.value=null
    }catch(cause){if(!disposed&&id===generation)error.value=parseLiveSoloError(cause,message('liveSolo.error.load')).displayMessage}
  }
  function open(action:Action){if(busy.value||!program.value||!actions.value.some(x=>x.action===action))return;decision.value={action,program:{...program.value}};reason.value=''}
  function setOpen(value:boolean){if(!value&&!busy.value)decision.value=null}
  async function confirm(){const target=decision.value;if(!target?.program.id||!target.program.concurrencyStamp||!reason.value.trim()||busy.value||!writable.value)return
    busy.value=true
    try{
      const result=await recoverLiveSoloProgram({path,body:{programId:target.program.id,expectedStamp:target.program.concurrencyStamp,action:target.action,reason:reason.value.trim()}})
      if(disposed)return
      if(result.error||!result.data)throw parseLiveSoloError(result.error,message('liveSolo.programControl.changed'))
      decision.value=null;program.value=result.data;error.value=null
    }catch(cause){if(!disposed){decision.value=null;error.value=parseLiveSoloError(cause,message('liveSolo.programControl.changed')).displayMessage}}
    finally{busy.value=false}
    if(!disposed)await load(false)
  }
  async function tick(){await load(false);if(!disposed)timer=setTimeout(tick,5000)}
  onMounted(()=>{void tick()});onScopeDispose(()=>{disposed=true;generation++;if(timer)clearTimeout(timer)})
  return {program,historyRows,error,busy,reason,decision,actionKey,stateKey,actions,load,open,setOpen,confirm}
}
export type LiveSoloProgramControlState=import('vue').ShallowUnwrapRef<ReturnType<typeof useLiveSoloProgramControl>>
