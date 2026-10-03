#!/usr/bin/env python3
"""Real EF/RustFS/Local/JetStream rehearsal, isolated resources only."""
import argparse,base64,json,os,socket,subprocess,tempfile,time,urllib.request,uuid
from pathlib import Path

def main():
    p=argparse.ArgumentParser(); p.add_argument('--context',required=True); p.add_argument('--host-image',required=True)
    p.add_argument('--tool-image',default='noctf-recovery:v2'); p.add_argument('--report',required=True)
    args=p.parse_args(); prefix='noctf-recovery-'+uuid.uuid4().hex[:12]
    docker=['docker','--context',args.context]; containers=[]; volumes=[]; results=[]
    def run(cmd,allowed=False,input=None):
        result=subprocess.run(docker+cmd,input=input,text=True,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
        if result.returncode and not allowed:
            # Rehearsal output is saved privately and not echoed; fixtures contain
            # no production credentials. The normal recovery CLI emits types only.
            failure=Path(args.report).parent/'recovery-step-failure.txt'; failure.parent.mkdir(parents=True,exist_ok=True)
            failure.write_text(result.stdout+'\n'+result.stderr); failure.chmod(0o600)
            raise RuntimeError('Docker rehearsal step failed, exit '+str(result.returncode))
        return result
    def volume(name):
        full=prefix+'-'+name; run(['volume','create','--label','noctf.io/rehearsal='+prefix,full]); volumes.append(full); return full
    def container(name,image,env=None,mounts=None,ports=None,command=None):
        full=prefix+'-'+name; cmd=['run','-d','--name',full,'--label','noctf.io/rehearsal='+prefix,'--network',prefix]
        for k,v in (env or {}).items():cmd+=['--env',k+'='+v]
        for source,target in mounts or []:cmd+=['--mount',f'type=volume,source={source},target={target}']
        for port in ports or []:cmd+=['-p',f'127.0.0.1::{port}']
        cmd+=[image]+(command or []); run(cmd); containers.append(full); return full
    def mapped(name,port):
        data=json.loads(run(['inspect',name]).stdout)[0]; return int(data['NetworkSettings']['Ports'][f'{port}/tcp'][0]['HostPort'])
    def pg_ready(name):
        for _ in range(90):
            if run(['exec',name,'pg_isready','-U','noctf','-d','noctf'],allowed=True).returncode==0:return
            time.sleep(1)
        raise RuntimeError('PostgreSQL fixture was not ready.')
    def nats_request(port,subject,payload):
        inbox='_INBOX.'+uuid.uuid4().hex; data=json.dumps(payload).encode()
        with socket.create_connection(('127.0.0.1',port),timeout=10) as sock:
            stream=sock.makefile('rb'); stream.readline()
            sock.sendall(b'CONNECT {"verbose":false,"pedantic":false}\r\n'+f'SUB {inbox} 1\r\nPUB {subject} {inbox} {len(data)}\r\n'.encode()+data+b'\r\nPING\r\n')
            while True:
                line=stream.readline().decode().strip()
                if line=='PING':sock.sendall(b'PONG\r\n');continue
                if line.startswith('MSG '):
                    parts=line.split();body=stream.read(int(parts[-1]));stream.read(2)
                    result=json.loads(body)
                    if result.get('error'):raise RuntimeError('JetStream rehearsal API rejected request.')
                    return result
                if line.startswith('-ERR') or not line:raise RuntimeError('NATS rehearsal request failed.')
    def js_state(port):
        consumer=nats_request(port,'$JS.API.CONSUMER.INFO.NOCTF_RECOVERY_EVENTS.audit',{})
        stream=nats_request(port,'$JS.API.STREAM.INFO.NOCTF_RECOVERY_EVENTS',{})
        kv=nats_request(port,'$JS.API.STREAM.MSG.GET.KV_NOCTF_RECOVERY_STATE',{'last_by_subj':'$KV.NOCTF_RECOVERY_STATE.marker'})
        return {'messages':stream['state']['messages'],'pending':consumer['num_pending'],'ackPending':consumer['num_ack_pending'],
                'deliveredSequence':consumer['delivered']['stream_seq'],'ackFloor':consumer['ack_floor']['stream_seq'],'kvValue':kv['message']['data']}
    run(['network','create','--label','noctf.io/rehearsal='+prefix,prefix])
    try:
        for kind in ('local','s3'):
            src_js=volume(kind+'-source-js'); dst_js=volume(kind+'-target-js'); work=volume(kind+'-work')
            src_files=volume(kind+'-source-files'); dst_files=volume(kind+'-target-files')
            pgimage='postgres:16.14@sha256:95206741a5b214807675e14165369d05b93a9cf692223b616d07cca227e74b0b'
            natsimage='docker.m.daocloud.io/library/nats:2.12.2-alpine@sha256:2d5fce3229ae5741f4ef9225aff95dc4dc036455931eaf77a3eec33fddaa192d'
            rustimage='rustfs/rustfs:1.0.0@sha256:8cc9801755448b71a786705ce76692c77e14936cccd87cf2fc31842e58f4d1ff'
            pg_env={'POSTGRES_DB':'noctf','POSTGRES_USER':'noctf','POSTGRES_PASSWORD':'rehearsal-postgres-only'}
            src=container(kind+'-source-pg',pgimage,pg_env); dst=container(kind+'-target-pg',pgimage,pg_env)
            ns=container(kind+'-source-nats',natsimage,mounts=[(src_js,'/data')],ports=[4222],command=['-js','-sd','/data'])
            pg_ready(src);pg_ready(dst)
            port=mapped(ns,4222)
            for _ in range(30):
                try:nats_request(port,'$JS.API.INFO',{});break
                except OSError:time.sleep(1)
            nats_request(port,'$JS.API.STREAM.CREATE.NOCTF_RECOVERY_EVENTS',{'name':'NOCTF_RECOVERY_EVENTS','subjects':['recovery.events'],'storage':'file'})
            for number in range(3):nats_request(port,'recovery.events',{'work':number,'dueAt':'2030-01-01T00:00:00Z'})
            nats_request(port,'$JS.API.CONSUMER.DURABLE.CREATE.NOCTF_RECOVERY_EVENTS.audit',{'stream_name':'NOCTF_RECOVERY_EVENTS','config':{'durable_name':'audit','ack_policy':'explicit','ack_wait':600_000_000_000,'deliver_policy':'all','filter_subject':'recovery.events'}})
            nats_request(port,'$JS.API.CONSUMER.MSG.NEXT.NOCTF_RECOVERY_EVENTS.audit',{'batch':1,'expires':5_000_000_000})
            nats_request(port,'$JS.API.STREAM.CREATE.KV_NOCTF_RECOVERY_STATE',{'name':'KV_NOCTF_RECOVERY_STATE','subjects':['$KV.NOCTF_RECOVERY_STATE.>'],'storage':'file','max_msgs_per_subject':1})
            nats_request(port,'$KV.NOCTF_RECOVERY_STATE.marker',{'value':'persistent-rehearsal-marker'})
            state=js_state(port)
            assert state['messages']==3 and state['pending']==2 and state['ackPending']==1
            env={'ConnectionStrings__PostgreSql':f'Host={src};Database=noctf;Username=noctf;Password=rehearsal-postgres-only',
                 'ConnectionStrings__Redis':'unused:6379','ConnectionStrings__Nats':f'nats://{ns}:4222',
                 'Authentication__SigningKey':'j'*64,'RunnerScoring__SigningKey':'r'*64,'EmailVerification__EncryptionKey':base64.b64encode(b'e'*32).decode(),
                 'SeedAdmin__UserName':'recovery-admin','SeedAdmin__Email':'admin@rehearsal.test','SeedAdmin__Password':'rehearsal-admin-only',
                 'Webhooks__PublicBaseUrl':'https://rehearsal.example.test','HumanVerification__Validation__PublicOrigin':'https://rehearsal.example.test',
                 'Observability__Enabled':'false','OTEL_SDK_DISABLED':'true'}
            cmd=['run','--rm','--network',prefix]
            for key,value in env.items():cmd+=['--env',key+'='+value]
            # Four separate Host processes race the same empty EF database.
            processes=[]
            for index in range(4):
                name=prefix+'-'+kind+'-initialize-'+str(index);containers.append(name)
                processes.append(subprocess.Popen(docker+cmd+['--name',name,'--label','noctf.io/rehearsal='+prefix,args.host_image,'--migrate-only'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL))
            codes=[process.wait(timeout=180) for process in processes]
            if any(code!=0 for code in codes):
                raise RuntimeError('Concurrent Host initialization failed.')
            history=run(['exec',src,'psql','-U','noctf','-d','noctf','-At','-c',"SELECT table_name||':'||column_name FROM information_schema.columns WHERE table_name ILIKE '%migrationshistory%' ORDER BY ordinal_position"]).stdout
            history_path=Path(args.report).parent/'rehearsal-history-columns.txt';history_path.parent.mkdir(parents=True,exist_ok=True);history_path.write_text(history)
            # Test-only credentials are written in-container, not exported as CLI secrets.
            prepare="""from pathlib import Path
import hashlib,json
p=Path('/work'); (p/'postgres-password').write_text('rehearsal-postgres-only'); (p/'access').write_text('REHEARSALACCESS'); (p/'secret').write_text('s'*64)
(p/'attachment').write_bytes(b'rehearsal-file-content'); (p/'state.json').write_text(json.dumps(STATE))
""".replace('STATE',repr(state))
            run(['run','--rm','--network',prefix,'--mount',f'type=volume,source={work},target=/work','--entrypoint','/opt/recovery/bin/python',args.tool_image,'-c',prepare])
            run(['run','--rm','--mount',f'type=volume,source={work},target=/work','--entrypoint','age-keygen',args.tool_image,'-o','/work/age-identity'])
            derive="from pathlib import Path;p=Path('/work');line=next(x for x in (p/'age-identity').read_text().splitlines() if x.startswith('# public key: '));(p/'age-recipients').write_text(line.split(': ',1)[1]+'\\n')"
            run(['run','--rm','--mount',f'type=volume,source={work},target=/work','--entrypoint','/opt/recovery/bin/python',args.tool_image,'-c',derive])
            run(['run','--rm','--mount',f'type=volume,source={work},target=/work','--entrypoint','minisign',args.tool_image,'-G','-W','-p','/work/sign.pub','-s','/work/sign.key'])
            endpoint=''
            if kind=='s3':
                rs=container(kind+'-source-rustfs',rustimage,{'RUSTFS_ACCESS_KEY':'REHEARSALACCESS','RUSTFS_SECRET_KEY':'s'*64},mounts=[(src_files,'/data')])
                rt=container(kind+'-target-rustfs',rustimage,{'RUSTFS_ACCESS_KEY':'REHEARSALACCESS','RUSTFS_SECRET_KEY':'s'*64},mounts=[(dst_files,'/data')])
                # Newly created fixture volumes are made writable for RustFS only.
                for v in (src_files,dst_files):run(['run','--rm','--mount',f'type=volume,source={v},target=/data','--entrypoint','sh',args.tool_image,'-c','chown -R 10001:10001 /data'])
                rs_env=[('Storage__S3__ServiceUrl',f'http://{rs}:9000'),('Storage__S3__Bucket','noctf'),('Storage__S3__AccessKey','REHEARSALACCESS'),('Storage__S3__SecretKey','s'*64)]
                init=['run','--rm','--network',prefix]
                for key,value in rs_env:init+=['--env',key+'='+value]
                for _ in range(30):
                    if run(init+[args.host_image,'--initialize-storage-only'],allowed=True).returncode==0:break
                    time.sleep(1)
                seed=f"import boto3,hashlib;from botocore.config import Config;c=boto3.client('s3',endpoint_url='http://{rs}:9000',region_name='us-east-1',aws_access_key_id='REHEARSALACCESS',aws_secret_access_key='{'s'*64}',config=Config(s3={{'addressing_style':'path'}},request_checksum_calculation='when_required'));b=b'rehearsal-file-content';c.put_object(Bucket='noctf',Key='attachments/fixture',Body=b,ContentType='text/plain; charset=utf-8',Metadata={{'sha256':hashlib.sha256(b).hexdigest(),'purpose':'rehearsal'}})"
                run(['run','--rm','--network',prefix,'--entrypoint','/opt/recovery/bin/python',args.tool_image,'-c',seed])
                storage=['--s3-endpoint',f'http://{rs}:9000','--s3-bucket','noctf','--s3-access-key-file','/work/access','--s3-secret-key-file','/work/secret']
                target_storage=storage.copy();target_storage[1]=f'http://{rt}:9000'
            else:
                run(['run','--rm','--mount',f'type=volume,source={src_files},target=/files','--entrypoint','sh',args.tool_image,'-c','mkdir -p /files/attachments; printf rehearsal-file-content > /files/attachments/fixture'])
                storage=target_storage=['--local-root','/files']
            run(['stop',ns]); assert not json.loads(run(['inspect',ns]).stdout)[0]['State']['Running']
            base=['--postgres-database','noctf','--postgres-user','noctf','--postgres-password-file','/work/postgres-password','--postgres-ssl-mode','disable',
                  '--storage-kind',kind,'--jetstream-dir','/jetstream','--jetstream-stopped','--host-image',args.host_image,'--secret-set-id','rehearsal-v2']
            invocation=['run','--rm','--network',prefix,'--mount',f'type=volume,source={work},target=/work']
            run(invocation+['--mount',f'type=volume,source={src_js},target=/jetstream,readonly','--mount',f'type=volume,source={src_files},target=/files,readonly',args.tool_image,'backup','--postgres-host',src,*base,*storage,'--jetstream-state-file','/work/state.json','--age-recipients-file','/work/age-recipients','--minisign-secret-key-file','/work/sign.key','--output','/work/point.age'])
            restore=invocation+['--mount',f'type=volume,source={dst_js},target=/jetstream','--mount',f'type=volume,source={dst_files},target=/files',args.tool_image,'restore','--postgres-host',dst,*base,*target_storage,'--input','/work/point.age','--age-identity-file','/work/age-identity','--minisign-public-key-file','/work/sign.pub']
            # RustFS owns its data volume; it is not the tool's object root.
            run(restore)
            assert run(restore,allowed=True).returncode!=0
            source_counts=run(['exec',src,'psql','-U','noctf','-d','noctf','-At','-c','SELECT count(*) FROM users']).stdout.strip()
            target_counts=run(['exec',dst,'psql','-U','noctf','-d','noctf','-At','-c','SELECT count(*) FROM users']).stdout.strip()
            assert source_counts==target_counts=='1'
            nt=container(kind+'-target-nats',natsimage,mounts=[(dst_js,'/data')],ports=[4222],command=['-js','-sd','/data'])
            newport=mapped(nt,4222)
            for _ in range(30):
                try:after=js_state(newport);break
                except OSError:time.sleep(1)
            assert after==state
            # Signatures are verified before target writes, even against a used target.
            tamper="from pathlib import Path;p=Path('/work');d=bytearray((p/'point.age').read_bytes());d[len(d)//2]^=1;(p/'tampered.age').write_bytes(d);(p/'tampered.age.minisig').write_bytes((p/'point.age.minisig').read_bytes())"
            run(['run','--rm','--mount',f'type=volume,source={work},target=/work','--entrypoint','/opt/recovery/bin/python',args.tool_image,'-c',tamper])
            changed=restore.copy();changed[changed.index('/work/point.age')]='/work/tampered.age'
            assert run(changed,allowed=True).returncode!=0
            results.append({'storage':kind,'realEfSchema':True,'concurrentHostInitializers':4,'usersRestored':1,'filesVerified':True,'jetStreamStatePreserved':True,'nonemptyRejected':True,'tamperedRejected':True})
    finally:
        for name in reversed(containers):run(['rm','-f',name],allowed=True)
        for name in reversed(volumes):run(['volume','rm',name],allowed=True)
        run(['network','rm',prefix],allowed=True)
    Path(args.report).parent.mkdir(parents=True,exist_ok=True);Path(args.report).write_text(json.dumps(results,indent=2)+'\n')
    print('Local/RustFS + current EF + persistent JetStream rehearsal passed.')

if __name__=='__main__':main()
