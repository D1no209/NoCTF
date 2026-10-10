import type { NoCtfapiEndpointsLiveSoloLiveSoloProgramHealthResponse as Health, NoCtfDomainLiveSoloLiveSoloProgramAction as Action } from '~/api'
export function availableProgramRecovery(program: Health|null,writable:boolean):Action[] {
  if(!program||!writable)return []
  const actions:Action[]=[]
  if(program.state==='RequiresReview')actions.push('ReconcileExport')
  if(program.state==='Completed')actions.push('RetryImport')
  if(['Active','Starting','Stopping','Completed','Failed'].includes(program.state??''))actions.push('Rotate')
  return actions
}
