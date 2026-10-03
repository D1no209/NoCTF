#!/usr/bin/env python3
"""Quiesced, encrypted PostgreSQL/files/JetStream recovery point, format 2."""
import argparse
import hashlib
import json
import os
import re
from pathlib import Path, PurePosixPath
import shutil
import subprocess
import sys
import tarfile
import tempfile
from datetime import datetime, timezone

SCHEMA='noctf.disaster-recovery/2'

def command(args, env=None):
    result=subprocess.run(args,env=env,text=True,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
    if result.returncode:
        state=re.search(r'(?:ERROR|FATAL):\s+([0-9A-Z]{5})\b',result.stderr) if Path(args[0]).name=='psql' else None
        raise ValueError(f'{Path(args[0]).name} failed (exit {result.returncode})'+(' SQLSTATE='+state[1] if state else '')+'; no command output or credentials are forwarded.')
    return result.stdout.strip()

def secret(path):
    value=Path(path).read_text().strip()
    if not value or '\n' in value or '\r' in value: raise ValueError('Secret file must contain one non-empty line.')
    return value

def sha(path):
    digest=hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda:stream.read(1024*1024),b''): digest.update(chunk)
    return digest.hexdigest()

def safe_key(value):
    path=PurePosixPath(value)
    if not value or path.is_absolute() or '\\' in value or any(part in ('','.','..') for part in value.split('/')): raise ValueError('Unsafe recovery entry path.')
    return path

def inventory(root):
    result=[]
    for path in sorted(root.rglob('*')):
        if path.is_symlink() or (not path.is_file() and not path.is_dir()): raise ValueError('Recovery directories cannot contain symlinks or special files.')
        if path.is_file(): result.append({'path':path.relative_to(root).as_posix(),'size':path.stat().st_size,'sha256':sha(path),'mode':path.stat().st_mode & 0o777})
    return result

def pg_env(args,temp):
    password=secret(args.postgres_password_file)
    escaped=lambda value:value.replace('\\','\\\\').replace(':','\\:')
    path=temp/'pgpass'
    path.write_text(':'.join(map(escaped,[args.postgres_host,str(args.postgres_port),args.postgres_database,args.postgres_user,password]))+'\n'); path.chmod(0o600)
    return {**os.environ,'PGHOST':args.postgres_host,'PGPORT':str(args.postgres_port),'PGDATABASE':args.postgres_database,
            'PGUSER':args.postgres_user,'PGPASSFILE':str(path),'PGSSLMODE':args.postgres_ssl_mode}

def sql(query,env):
    return command(['psql','-X','--no-psqlrc','--set=ON_ERROR_STOP=1','--set=VERBOSITY=sqlstate','--tuples-only','--no-align','--command',query],env)

def quiesced(env):
    if sql("SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid() AND backend_type='client backend'",env)!='0':
        raise ValueError('Stop API, Worker, Runner and other database writers before backup/restore.')

def tables(env):
    names=sql("SELECT quote_ident(n.nspname)||'.'||quote_ident(c.relname) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE c.relkind IN ('r','p') AND n.nspname='public' ORDER BY c.relname",env).splitlines()
    return {name:int(sql('SELECT count(*) FROM '+name,env)) for name in names}

def migrations(env):
    if sql("SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL",env)!='t': raise ValueError('Source must have the current EF-created application schema.')
    column=sql("SELECT column_name FROM information_schema.columns WHERE table_schema='public' AND table_name='__EFMigrationsHistory' AND replace(lower(column_name),'_','')='migrationid'",env)
    if column not in ('MigrationId','migration_id'): raise ValueError('Unexpected EF migration history columns.')
    return sql('SELECT "'+column+'" FROM public."__EFMigrationsHistory" ORDER BY "'+column+'"',env).splitlines()

def s3(args):
    import boto3
    from botocore.config import Config
    return boto3.client('s3',endpoint_url=args.s3_endpoint,region_name=args.s3_region,
        aws_access_key_id=secret(args.s3_access_key_file),aws_secret_access_key=secret(args.s3_secret_key_file),
        config=Config(s3={'addressing_style':'path'},connect_timeout=10,read_timeout=60,retries={'max_attempts':2},
                      request_checksum_calculation='when_required',response_checksum_validation='when_required'))

def object_keys(client,bucket):
    return sorted(obj['Key'] for page in client.get_paginator('list_objects_v2').paginate(Bucket=bucket) for obj in page.get('Contents',[]))

def backup_storage(args,bundle):
    target=bundle/'files'; target.mkdir()
    if args.storage_kind=='local':
        source=Path(args.local_root)
        if not source.is_dir(): raise ValueError('Local object root is missing.')
        records=inventory(source)
        for record in records:
            path=target/safe_key(record['path']); path.parent.mkdir(parents=True,exist_ok=True); shutil.copyfile(source/record['path'],path)
            if sha(path)!=record['sha256']: raise ValueError('Local files changed during backup.')
        if inventory(source)!=records: raise ValueError('Local files changed during backup.')
        return records
    client=s3(args); records=[]
    for key in object_keys(client,args.s3_bucket):
        path=target/safe_key(key); path.parent.mkdir(parents=True,exist_ok=True)
        client.download_file(args.s3_bucket,key,str(path))
        info=client.head_object(Bucket=args.s3_bucket,Key=key)
        record={'path':key,'size':path.stat().st_size,'sha256':sha(path),'metadata':info.get('Metadata',{}),'contentType':info.get('ContentType','application/octet-stream')}
        for header in ('CacheControl','ContentDisposition','ContentEncoding','ContentLanguage'):
            if header in info: record[header]=info[header]
        supplied=record['metadata'].get('sha256')
        if supplied and supplied.lower()!=record['sha256']: raise ValueError('Source object checksum metadata does not match its content.')
        records.append(record)
    return records

def assert_empty_dir(path):
    if path.is_symlink() or path.resolve()==Path('/'): raise ValueError('Unsafe recovery target directory.')
    path.mkdir(parents=True,exist_ok=True)
    if any(path.iterdir()): raise ValueError('Recovery target directory is not empty.')

def directory_restore(records,source,target):
    for record in records:
        path=target/safe_key(record['path']); path.parent.mkdir(parents=True,exist_ok=True)
        original=source/safe_key(record['path'])
        if sha(original)!=record['sha256']: raise ValueError('Recovery file checksum mismatch.')
        shutil.copyfile(original,path); path.chmod(record.get('mode',0o600))

def verify(args,bundle,manifest,env):
    if tables(env)!=manifest['postgres']['tables'] or migrations(env)!=manifest['postgres']['migrations']: raise ValueError('Restored database counts or migration history differ.')
    records=manifest['storage']['files']
    if args.storage_kind=='local':
        actual=inventory(Path(args.local_root))
        select=lambda values:[{k:r[k] for k in ('path','size','sha256')} for r in values]
        if select(actual)!=select(records): raise ValueError('Restored local file inventory differs.')
    else:
        client=s3(args)
        if object_keys(client,args.s3_bucket)!=[r['path'] for r in records]: raise ValueError('Restored object keys differ.')
        with tempfile.TemporaryDirectory() as temp:
            for record in records:
                path=Path(temp)/'object'; client.download_file(args.s3_bucket,record['path'],str(path))
                info=client.head_object(Bucket=args.s3_bucket,Key=record['path'])
                if sha(path)!=record['sha256'] or info.get('Metadata',{})!=record['metadata'] or info.get('ContentType')!=record['contentType']: raise ValueError('Restored object content/metadata differs.')
                for header in ('CacheControl','ContentDisposition','ContentEncoding','ContentLanguage'):
                    if info.get(header)!=record.get(header): raise ValueError('Restored object headers differ.')
    actual=inventory(Path(args.jetstream_dir))
    if actual!=manifest['jetstream']['files']: raise ValueError('Restored offline JetStream inventory differs.')

def backup(args):
    output=Path(args.output); signature=Path(str(output)+'.minisig')
    if output.exists() or signature.exists(): raise ValueError('Backup output or signature already exists.')
    if not args.jetstream_stopped: raise ValueError('NATS must be stopped; confirm --jetstream-stopped after stopping it.')
    js=Path(args.jetstream_dir)
    if not js.is_dir() or not inventory(js): raise ValueError('JetStream data is missing or empty.')
    if not args.host_image or '@sha256:' not in args.host_image: raise ValueError('Record the exact Host image digest.')
    output.parent.mkdir(parents=True,exist_ok=True)
    partial=Path(str(output)+'.partial')
    try:
        with tempfile.TemporaryDirectory() as temp:
            temp=Path(temp); env=pg_env(args,temp); quiesced(env)
            bundle=temp/'noctf-recovery-v2'; (bundle/'postgres').mkdir(parents=True)
            before=tables(env); history=migrations(env)
            command(['pg_dump','--format=custom','--schema=public','--no-owner','--no-privileges','--file',str(bundle/'postgres/database.dump')],env)
            files=backup_storage(args,bundle)
            js_before=inventory(js); shutil.copytree(js,bundle/'jetstream')
            if inventory(js)!=js_before or inventory(bundle/'jetstream')!=js_before: raise ValueError('JetStream changed during its offline copy.')
            quiesced(env)
            if tables(env)!=before: raise ValueError('Database changed while producing recovery point.')
            manifest={'schemaVersion':SCHEMA,'createdAt':datetime.now(timezone.utc).isoformat(),'consistency':'application-and-nats-stopped',
                      'secretSetId':args.secret_set_id,'hostImage':args.host_image,'postgres':{'database':args.postgres_database,'serverVersion':sql('SHOW server_version',env),'tables':before,'migrations':history,'dumpSha256':sha(bundle/'postgres/database.dump')},
                      'storage':{'kind':args.storage_kind,'bucket':args.s3_bucket if args.storage_kind=='s3' else None,'files':files},
                      'jetstream':{'files':js_before,'state':json.loads(Path(args.jetstream_state_file).read_text()) if args.jetstream_state_file else None},
                      'redis':{'included':False,'recovery':'rebuild-cache'},'secrets':{'included':False,'externalSetRequired':True}}
            (bundle/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
            archive=temp/'point.tar.gz'
            with tarfile.open(archive,'w:gz') as tar: tar.add(bundle,arcname=bundle.name)
            command(['age','--encrypt','--recipients-file',args.age_recipients_file,'--output',str(partial),str(archive)])
            command(['minisign','-S','-s',args.minisign_secret_key_file,'-m',str(partial),'-x',str(signature)+'.partial'])
            os.replace(partial,output); os.replace(str(signature)+'.partial',signature); output.chmod(0o600); signature.chmod(0o600)
    finally:
        partial.unlink(missing_ok=True); Path(str(signature)+'.partial').unlink(missing_ok=True)
    print('Encrypted, signed PostgreSQL/files/offline-JetStream recovery point created.')

def restore(args):
    if not args.jetstream_stopped: raise ValueError('Keep NATS stopped during restore.')
    with tempfile.TemporaryDirectory() as temp:
        temp=Path(temp)
        command(['minisign','-V','-q','-p',args.minisign_public_key_file,'-m',args.input,'-x',args.input+'.minisig'])
        archive=temp/'point.tar.gz'
        command(['age','--decrypt','--identity',args.age_identity_file,'--output',str(archive),args.input])
        with tarfile.open(archive,'r:gz') as tar:
            for member in tar.getmembers():
                path=safe_key(member.name)
                if path.parts[0]!='noctf-recovery-v2' or not (member.isfile() or member.isdir()): raise ValueError('Unsupported or unsafe archive entry.')
            tar.extractall(temp,filter='data')
        bundle=temp/'noctf-recovery-v2'; manifest=json.loads((bundle/'manifest.json').read_text())
        if manifest['schemaVersion']!=SCHEMA: raise ValueError('Unsupported recovery format; legacy points do not include complete JetStream state.')
        if manifest['secretSetId']!=args.secret_set_id: raise ValueError('External Secret set does not match.')
        if manifest['storage']['kind']!=args.storage_kind or manifest['postgres']['database']!=args.postgres_database: raise ValueError('Recovery target type/database does not match.')
        if args.storage_kind=='s3' and manifest['storage']['bucket']!=args.s3_bucket: raise ValueError('Target bucket differs.')
        if args.host_image!=manifest['hostImage']: raise ValueError('Restore with the recovery point Host image before considering an upgrade.')
        if sha(bundle/'postgres/database.dump')!=manifest['postgres']['dumpSha256']: raise ValueError('Database dump checksum differs.')
        env=pg_env(args,temp); quiesced(env)
        if tables(env): raise ValueError('Target database is not empty.')
        if sql('SHOW server_version',env).split('.')[0]!=manifest['postgres']['serverVersion'].split('.')[0]: raise ValueError('PostgreSQL major version differs.')
        js=Path(args.jetstream_dir); assert_empty_dir(js)
        for record in manifest['storage']['files']:
            if sha(bundle/'files'/safe_key(record['path']))!=record['sha256']: raise ValueError('Object/file checksum differs.')
        if inventory(bundle/'jetstream')!=manifest['jetstream']['files']: raise ValueError('JetStream bundle differs.')
        if args.storage_kind=='local': assert_empty_dir(Path(args.local_root))
        else:
            client=s3(args)
            from botocore.exceptions import ClientError
            try: client.head_bucket(Bucket=args.s3_bucket)
            except ClientError as error:
                if error.response['ResponseMetadata']['HTTPStatusCode']!=404: raise
                client.create_bucket(Bucket=args.s3_bucket)
            if object_keys(client,args.s3_bucket): raise ValueError('Target S3 bucket is not empty.')
        # pg_dump --schema=public includes CREATE SCHEMA public. Only an empty
        # verified target may remove its default schema before transactional restore.
        sql('DROP SCHEMA public',env)
        command(['pg_restore','--exit-on-error','--single-transaction','--no-owner','--no-privileges','--dbname',args.postgres_database,str(bundle/'postgres/database.dump')],env)
        if args.storage_kind=='local': directory_restore(manifest['storage']['files'],bundle/'files',Path(args.local_root))
        else:
            for record in manifest['storage']['files']:
                extra={'Metadata':record['metadata'],'ContentType':record['contentType']}
                for header in ('CacheControl','ContentDisposition','ContentEncoding','ContentLanguage'):
                    if header in record: extra[header]=record[header]
                with (bundle/'files'/safe_key(record['path'])).open('rb') as stream: client.upload_fileobj(stream,args.s3_bucket,record['path'],ExtraArgs=extra)
        directory_restore(manifest['jetstream']['files'],bundle/'jetstream',js)
        verify(args,bundle,manifest,env); quiesced(env)
    print('Restore verified. Keep writers stopped until live JetStream and application acceptance complete.')

def parser():
    p=argparse.ArgumentParser(description='Quiesced recovery point v2; credentials are file-only.')
    p.add_argument('command',choices=('backup','restore'))
    for name in ('postgres-host','postgres-database','postgres-user','postgres-password-file','jetstream-dir','host-image','secret-set-id'): p.add_argument('--'+name,required=True)
    p.add_argument('--postgres-port',type=int,default=5432); p.add_argument('--postgres-ssl-mode',default='require')
    p.add_argument('--storage-kind',choices=('local','s3'),required=True); p.add_argument('--local-root')
    for name in ('s3-endpoint','s3-bucket','s3-access-key-file','s3-secret-key-file','jetstream-state-file','output','age-recipients-file','minisign-secret-key-file','input','age-identity-file','minisign-public-key-file'): p.add_argument('--'+name)
    p.add_argument('--s3-region',default='us-east-1'); p.add_argument('--jetstream-stopped',action='store_true')
    return p

def main():
    os.umask(0o077); args=parser().parse_args()
    required=['local_root'] if args.storage_kind=='local' else ['s3_endpoint','s3_bucket','s3_access_key_file','s3_secret_key_file']
    required+=['output','age_recipients_file','minisign_secret_key_file'] if args.command=='backup' else ['input','age_identity_file','minisign_public_key_file']
    if any(not getattr(args,name) for name in required): raise ValueError('Required storage/encryption arguments are missing.')
    (backup if args.command=='backup' else restore)(args)

if __name__=='__main__':
    try: main()
    except Exception as error:
        # SDK/server errors may contain payloads. Report type only, never bodies.
        print('Recovery failed: '+(str(error) if isinstance(error,ValueError) else type(error).__name__),file=sys.stderr); sys.exit(1)
