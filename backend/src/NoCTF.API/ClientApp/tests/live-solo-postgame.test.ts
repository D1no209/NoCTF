import {describe,expect,test} from 'bun:test'
import {postgameQuestion,postgameReviewVersion} from '../app/features/live-solo/postgame-state'
describe('LiveSolo post-event scopes',()=>{
  test('question lookup requires the exact historical round and never guesses another scope',()=>{
    const questions=[{id:'question',roundId:'round',title:'Question'}]
    expect(postgameQuestion(questions,'question','round')?.title).toBe('Question')
    expect(postgameQuestion(questions,'question','another')).toBeNull();expect(postgameQuestion(questions,'missing','round')).toBeNull()
  })
  test('withdrawal targets the public version even when a newer submission exists',()=>{
    const root={published:{id:'public',state:'Approved' as const},submitted:{id:'pending',state:'Submitted' as const}}
    expect(postgameReviewVersion(root,'Withdraw')).toBe('public');expect(postgameReviewVersion(root,'Publish')).toBe('pending')
    expect(postgameReviewVersion(null,'Withdraw')).toBeNull()
  })
})
