import { expect, test } from 'bun:test'
import { createMockApi } from './api'
import { id } from './schema'

test('management announcements update and withdraw the same inbox record', async () => {
  const api=createMockApi()
  const base='http://127.0.0.1:5081'
  const login=await api.handle(new Request(base+'/api/v1/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({login:'admin',password:'Mock123!'})}))
  const token=(await login.json()).accessToken
  const send=(path:string,method='GET',body?:unknown)=>api.handle(new Request(base+path,{method,headers:{'Authorization':`Bearer ${token}`,'Content-Type':'application/json'},...(body?{body:JSON.stringify(body)}:{})}))
  const path=`/api/v1/admin/competitions/${id(2)}/announcements`
  const created=await send(path,'POST',{title:'First title',body:'First body',audience:'Participants'})
  expect(created.status).toBe(201)
  const original=await created.json()
  expect(original.content.title).toBe('First title')
  const list=await (await send(path+'?offset=0&limit=10&desc=true&includeWithdrawn=false')).json()
  expect(list.items[0].id).toBe(original.id)
  expect(list.items[0].audience).toBe('Participants')
  const changed=await send(path+'/'+original.id,'PATCH',{title:'Changed title',body:'Changed body'})
  expect(changed.status).toBe(200)
  const inbox=await (await send('/api/v1/notifications?scope=Inbox&offset=0&limit=50&desc=true')).json()
  expect(inbox.items.find((item:any)=>item.id===original.id).content.body).toBe('Changed body')
  expect((await send(path+'/'+original.id,'DELETE')).status).toBe(204)
  const withdrawn=await (await send('/api/v1/notifications?scope=Inbox&offset=0&limit=50&desc=true')).json()
  expect(withdrawn.items.some((item:any)=>item.id===original.id)).toBe(false)
  const history=await (await send(path+'?offset=0&limit=10&desc=true&includeWithdrawn=true')).json()
  expect(history.items[0].state).toBe('Withdrawn')
  expect((await send(path+'/'+original.id,'PATCH',{title:'Resurrect',body:'body'})).status).toBe(409)
  expect((await send(path+'/'+original.id,'DELETE')).status).toBe(204)
})
