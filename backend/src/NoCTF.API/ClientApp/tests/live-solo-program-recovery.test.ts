import {expect,test} from 'bun:test'
import {availableProgramRecovery} from '../app/features/live-solo/program-recovery-policy'
test('unknown programme starts offer reconciliation only, while live rotation cannot directly claim completion',()=>{
 expect(availableProgramRecovery({state:'RequiresReview'},true)).toEqual(['ReconcileExport'])
 expect(availableProgramRecovery({state:'Active',stalled:true},true)).toEqual(['Rotate'])
 expect(availableProgramRecovery({state:'Starting'},true)).toEqual(['Rotate'])
 expect(availableProgramRecovery({state:'Completed'},true)).toEqual(['RetryImport','Rotate'])
 expect(availableProgramRecovery({state:'Failed'},true)).toEqual(['Rotate'])
})
test('observer and judge programme controls remain read-only and pending work cannot be started twice',()=>{
 expect(availableProgramRecovery({state:'Active'},false)).toEqual([])
 expect(availableProgramRecovery({state:'Pending'},true)).toEqual([])
 expect(availableProgramRecovery(null,true)).toEqual([])
})
