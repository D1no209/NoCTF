#!/usr/bin/env python3
"""Configuration generation and deployment shared by the Bash entrypoints."""
import argparse
import hashlib
import ipaddress
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import time
from datetime import datetime, timezone
from urllib.parse import urlsplit

ROOT = Path(__file__).resolve().parents[1]
SECRET_KEYS = ('POSTGRES_PASSWORD', 'JWT_SECRET', 'RUNNER_SCORING_SECRET', 'EMAIL_VERIFICATION_ENCRYPTION_KEY',
               'SEED_ADMIN_PASSWORD', 'REGISTRY_HTTP_SECRET', 'REGISTRY_PASSWORD', 'S3_ACCESS_KEY', 'S3_SECRET_KEY', 'GRAFANA_PASSWORD')

def run(args, settings, cwd=None, capture=True):
    result = subprocess.run(args, cwd=cwd, text=True, stdout=subprocess.PIPE if capture else None, stderr=subprocess.PIPE)
    if result.returncode:
        # CLI output can contain expanded configuration. Never forward it verbatim.
        raise ValueError(f'{Path(args[0]).name} failed (exit {result.returncode}); inspect redacted status with the deployment verification entrypoint.')
    return result.stdout or ''

def diagnose(settings):
    """Status only: no specs, environment, logs, event messages or Secret data."""
    try:
        if settings.get('target')=='docker':
            text=run(compose(settings)+['ps','--all','--format','json'],settings)
            try: rows=json.loads(text or '[]')
            except json.JSONDecodeError: rows=[json.loads(line) for line in text.splitlines() if line.strip()]
            if isinstance(rows,dict): rows=[rows]
            safe=[{key:row.get(key) for key in ('Name','Service','State','Health','ExitCode')} for row in rows]
        else:
            rows=json.loads(run(['kubectl','--context',settings['context'],'get','pods','-n','noctf','-o','json'],settings))['items']
            safe=[{'pod':row['metadata']['name'],'phase':row.get('status',{}).get('phase'),
                   'conditions':[{'type':c.get('type'),'status':c.get('status'),'reason':c.get('reason')} for c in row.get('status',{}).get('conditions',[])]} for row in rows]
        print(json.dumps({'redactedStatus':safe},ensure_ascii=False),file=sys.stderr)
    except Exception:
        print('Redacted status diagnostics were unavailable.',file=sys.stderr)

def parse_env(path):
    values = {}
    if not path.is_file(): return values
    for line in path.read_text().splitlines():
        if not line or line.startswith('#') or '=' not in line: continue
        key, value = line.split('=', 1)
        if not re.fullmatch(r'[A-Za-z_][A-Za-z0-9_]*', key): continue
        if value.startswith("'") and value.endswith("'"): value = value[1:-1].replace("\\'", "'").replace('\\\\', '\\')
        elif value.startswith('"') and value.endswith('"'):
            value=json.loads(value).replace('$$','$')
        if '${' not in value: values[key] = value
    return values

def load(directory):
    path = Path(directory)
    state = path / 'installation.json'
    values = json.loads(state.read_text()) if state.is_file() else {}
    for key, value in parse_env(path / '.env').items(): values.setdefault(key, value)
    auth=path/'config/docker/config.json'
    if auth.is_file() and not values.get('REGISTRY_PASSWORD'):
        import base64
        for hostname, record in json.loads(auth.read_text()).get('auths',{}).items():
            if record.get('auth'):
                username,password=base64.b64decode(record['auth']).decode().split(':',1)
                values.setdefault('REGISTRY_HOST',hostname); values.setdefault('REGISTRY_USERNAME',username); values.setdefault('REGISTRY_PASSWORD',password)
                break
    return values

def validate(s):
    if s.get('target') not in ('docker', 'kubernetes'): raise ValueError('Select docker or kubernetes.')
    directory = Path(s['directory']).expanduser().absolute()
    if directory == Path('/') or directory in (Path('/etc'), Path('/usr'), Path('/var'), Path.home()):
        raise ValueError('Use a dedicated installation directory.')
    if directory.is_symlink(): raise ValueError('Installation directory cannot be a symlink.')
    if directory==ROOT or directory.is_relative_to(ROOT): raise ValueError('Generated installations and credentials must be outside deploy source directories.')
    repository=ROOT.parent
    if directory.is_relative_to(repository) and not directory.is_relative_to(repository/'.codex'):
        raise ValueError('Installations and secrets must be outside the repository; only ignored acceptance state may use .codex.')
    if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9./:_-]*@sha256:[a-f0-9]{64}', s['NOCTF_PLATFORM_IMAGE']):
        raise ValueError('Host image requires repository@sha256:<64 hex characters>.')
    if not s.get('context'): raise ValueError('An explicit context is required.')
    if not re.fullmatch(r'[a-z0-9][a-z0-9_-]{0,62}', s['COMPOSE_PROJECT_NAME']): raise ValueError('Invalid project name.')
    for key in ('NOCTF_PUBLIC_URL',):
        url = urlsplit(s[key])
        if url.scheme != 'https' or not url.hostname or url.username or url.password or url.query or url.fragment or url.path not in ('','/'):
            raise ValueError(f'{key} requires an HTTPS origin.')
    if urlsplit(s['NOCTF_PUBLIC_URL']).hostname != s['NOCTF_PUBLIC_HOST']: raise ValueError('Public URL and hostname differ.')
    for key in ('NOCTF_PUBLIC_HOST','DOCKER_PUBLISHED_HOST'):
        if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9.-]*',s[key]): raise ValueError(f'{key} must be a hostname or IPv4 address.')
    ipaddress.ip_network(s['NOCTF_PROXY_NETWORK'], strict=False)
    for key in SECRET_KEYS:
        if any(char in s.get(key, '') for char in '\r\n\x00'): raise ValueError('Secrets must be single-line values.')
    for key in ('POSTGRES_PASSWORD','JWT_SECRET','RUNNER_SCORING_SECRET','REGISTRY_HTTP_SECRET'):
        if len(s[key]) < 32: raise ValueError(f'{key} requires at least 32 characters.')
    import base64
    if len(base64.b64decode(s['EMAIL_VERIFICATION_ENCRYPTION_KEY'], validate=True)) != 32: raise ValueError('Encryption key must decode to 32 bytes.')
    if not re.fullmatch(r'[A-Za-z0-9_-]{3,64}',s['SEED_ADMIN_USERNAME']): raise ValueError('Invalid administrator name.')
    if '@' not in s['SEED_ADMIN_EMAIL'] or not 8 <= len(s['SEED_ADMIN_PASSWORD']) <= 1024: raise ValueError('Invalid administrator email/password.')
    if s['target']=='docker':
        if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9._-]*',s['REGISTRY_USERNAME']): raise ValueError('Invalid Registry username.')
        if len(s['REGISTRY_PASSWORD']) < 8: raise ValueError('Registry password requires at least 8 characters.')
    if s.get('storage')=='s3':
        if not s.get('S3_ACCESS_KEY') or len(s.get('S3_SECRET_KEY',''))<32: raise ValueError('S3 credentials are required.')
        if urlsplit(s['S3_PUBLIC_URL']).scheme!='https': raise ValueError('S3 public origin must use HTTPS.')
    if s.get('monitoring')=='yes' and s['target']=='docker':
        if urlsplit(s.get('GRAFANA_PUBLIC_URL','')).scheme!='https' or not s.get('GRAFANA_PUBLIC_HOST'): raise ValueError('Grafana HTTPS origin/hostname are required.')
    if s['target']=='kubernetes':
        import yaml
        for key in ('tlsCert','tlsKey'):
            if not Path(s[key]).is_file(): raise ValueError('Existing TLS certificate and private key files are required.')
        ipaddress.ip_address(s['clusterDns'])
        for cidr in s['protectedCidrs'].split(','): ipaddress.ip_network(cidr.strip(),strict=False)
        if not re.fullmatch(r'[a-z0-9][a-z0-9.-]*',s['storageClass']): raise ValueError('Invalid storage class.')
        for hostname in (s['NOCTF_PUBLIC_HOST'],urlsplit(s['S3_PUBLIC_URL']).hostname):
            run(['openssl','x509','-in',s['tlsCert'],'-noout','-checkhost',hostname],s)
        cert_key=run(['openssl','x509','-in',s['tlsCert'],'-pubkey','-noout'],s)
        private_key=run(['openssl','pkey','-in',s['tlsKey'],'-pubout'],s)
        if cert_key != private_key: raise ValueError('TLS certificate and key do not match.')
    return directory

def env_text(values):
    return ''.join(key+'='+json.dumps(str(value).replace('$','$$'),ensure_ascii=False)+'\n' for key,value in values.items())

def write(path, text, mode=0o600):
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(text); path.chmod(mode)

def copy_tree(source, target):
    shutil.copytree(source,target,dirs_exist_ok=True,ignore=shutil.ignore_patterns('__pycache__','*.pyc'))

def generate_docker(s, stage, directory):
    import base64
    defaults=parse_env(ROOT/'docker/.env.example'); defaults.update(parse_env(directory/'.env')); defaults.update({key:value for key,value in s.items() if key.isupper()})
    defaults['NOCTF_SHARED_ROOT']=str(directory/'shared/observability')
    write(stage/'.env',env_text(defaults))
    for name in ('docker-compose.yml','compose.rustfs.yml','compose.monitoring.yml'): shutil.copy2(ROOT/'docker'/name,stage/name)
    for name in ('noctf','postgres','registry'):
        template=(ROOT/'docker/env'/name/'.env.example').read_text()
        managed={line.split('=',1)[0] for line in template.splitlines() if '=' in line and not line.startswith('#')}
        old=directory/f'env/{name}/.env'
        if old.is_file():
            extra=[line for line in old.read_text().splitlines() if '=' in line and line.split('=',1)[0] not in managed and not line.startswith('#')]
            if extra: template+='\n# Preserved operator settings\n'+'\n'.join(extra)+'\n'
        write(stage/f'env/{name}/.env',template)
    # Connection-string quoting is independent from dotenv quoting. Do not
    # concatenate a password containing semicolons or quotes into its grammar.
    quote=lambda value:'"'+str(value).replace('"','""')+'"'
    connection='Host=postgres;Port=5432;Database='+quote(defaults['POSTGRES_DB'])+';Username='+quote(defaults['POSTGRES_USER'])+';Password='+quote(s['POSTGRES_PASSWORD'])
    env_path=stage/'env/noctf/.env'
    env_source=env_path.read_text()
    env_source=re.sub(r'(?m)^ConnectionStrings__PostgreSql=.*$',lambda _:env_text({'ConnectionStrings__PostgreSql':connection}).strip(),env_source)
    write(env_path,env_source)
    copy_tree(ROOT/'shared/observability',stage/'shared/observability')
    copy_tree(ROOT/'docker/observability',stage/'observability')
    obs=parse_env(ROOT/'docker/observability/.env.example')
    obs.update(NOCTF_NETWORK_NAME=s['COMPOSE_PROJECT_NAME']+'-network',NOCTF_SHARED_ROOT=str(directory/'shared/observability'),
               OBSERVABILITY_PROJECT_NAME=s['COMPOSE_PROJECT_NAME']+'-observability',OBSERVABILITY_DATA_ROOT=str(directory/'data/observability'),
               OBSERVABILITY_SECRET_ROOT=str(directory/'config/observability'),GRAFANA_PUBLIC_HOST=s.get('GRAFANA_PUBLIC_HOST','grafana.invalid'),
               GRAFANA_PUBLIC_URL=s.get('GRAFANA_PUBLIC_URL','https://grafana.invalid'),POSTGRES_EXPORTER_USER='noctf_monitor',
               POSTGRES_EXPORTER_URI='postgres:5432/noctf?sslmode=disable')
    write(stage/'observability/.env',env_text(obs))
    write(stage/'config/observability/grafana-admin-password',s['GRAFANA_PASSWORD'])
    write(stage/'config/observability/postgres-exporter-password',s.get('MONITOR_PASSWORD',s['GRAFANA_PASSWORD']))
    result=subprocess.run(['htpasswd','-niB',s['REGISTRY_USERNAME']],input=s['REGISTRY_PASSWORD']+'\n',text=True,stdout=subprocess.PIPE,stderr=subprocess.PIPE)
    if result.returncode: raise ValueError('Registry bcrypt generation failed.')
    existing=directory/'config/registry/auth/htpasswd'
    write(stage/'config/registry/auth/htpasswd',existing.read_text() if existing.is_file() else result.stdout)
    registry=s['REGISTRY_HOST']
    write(stage/'config/docker/config.json',json.dumps({'auths':{registry:{'auth':base64.b64encode((s['REGISTRY_USERNAME']+':'+s['REGISTRY_PASSWORD']).encode()).decode()}}},indent=2)+'\n')
    copy_tree(ROOT/'docker/nginx',stage/'nginx')
    write(stage/'nginx/noctf.conf',(stage/'nginx/noctf.conf').read_text().replace('noctf.example.com',s['NOCTF_PUBLIC_HOST']),0o644)

def generate_kubernetes(s, stage, directory):
    import yaml
    copy_tree(ROOT/'k8s',stage/'k8s')
    copy_tree(ROOT/'shared',stage/'shared')
    custom=stage/'k8s/overlays/configured'; custom.mkdir(parents=True,exist_ok=True)
    data={'Database__AutoMigrate':'true','SeedAdmin__UserName':s['SEED_ADMIN_USERNAME'],'SeedAdmin__Email':s['SEED_ADMIN_EMAIL'],
          'Storage__PublicBaseUrl':s['S3_PUBLIC_URL'],'Storage__S3__Bucket':s['S3_BUCKET'],'Webhooks__PublicBaseUrl':s['NOCTF_PUBLIC_URL'],
          'Authentication__RefreshAllowedOrigins__0':s['NOCTF_PUBLIC_URL'],'Cors__AllowedOrigins__0':s['NOCTF_PUBLIC_URL'],
          'HumanVerification__Validation__PublicOrigin':s['NOCTF_PUBLIC_URL'],'ForwardedHeaders__KnownNetworks__0':s['NOCTF_PROXY_NETWORK'],
          'ForwardedHeaders__AllowedHosts__0':s['NOCTF_PUBLIC_HOST'],'Runtime__Kubernetes__PublicHost':s['DOCKER_PUBLISHED_HOST'],
          'Runtime__Kubernetes__ClusterDnsServiceAddress':s['clusterDns'],'Runtime__Kubernetes__ClusterDomain':s.get('clusterDomain','cluster.local')}
    for index,cidr in enumerate(s['protectedCidrs'].split(',')): data[f'Runtime__Kubernetes__ProtectedCidrs__{index}']=cidr.strip()
    patches=[{'target':{'kind':'ConfigMap','name':'noctf-config'},'patch':json.dumps({'apiVersion':'v1','kind':'ConfigMap','metadata':{'name':'noctf-config','namespace':'noctf'},'data':data})},
             {'target':{'kind':'HTTPRoute','name':'platform'},'patch':json.dumps([{'op':'replace','path':'/spec/hostnames','value':[s['NOCTF_PUBLIC_HOST']]}])},
             {'target':{'kind':'HTTPRoute','name':'files'},'patch':json.dumps([{'op':'replace','path':'/spec/hostnames','value':[urlsplit(s['S3_PUBLIC_URL']).hostname]}])},
             {'target':{'kind':'PersistentVolumeClaim'},'patch':json.dumps([{'op':'replace','path':'/spec/storageClassName','value':s['storageClass']}])}]
    for name,kind in (('backend','Deployment'),('worker','Deployment'),('runner','StatefulSet')):
        patches.append({'target':{'kind':kind,'name':name},'patch':json.dumps({'apiVersion':'apps/v1','kind':kind,'metadata':{'name':name,'namespace':'noctf'},'spec':{'template':{'spec':{'containers':[{'name':name,'env':[{'name':'ConnectionStrings__PostgreSql','value':None,'valueFrom':{'secretKeyRef':{'name':'noctf-secrets','key':'postgres-connection'}}}]}]}}}})})
        if s.get('pullConfig'):
            patches.append({'target':{'kind':kind,'name':name},'patch':json.dumps({'apiVersion':'apps/v1','kind':kind,'metadata':{'name':name,'namespace':'noctf'},'spec':{'template':{'spec':{'imagePullSecrets':[{'name':'host-registry'}]}}}})})
    if not s.get('pullConfig'):
        patches[0]['patch']=json.dumps({**json.loads(patches[0]['patch']),'data':{**data,'Runtime__Kubernetes__ImagePullSecrets__0':None}})
    write(custom/'kustomization.yaml',json.dumps({'apiVersion':'kustomize.config.k8s.io/v1beta1','kind':'Kustomization',
          'resources':['../production'],'images':[{'name':'noctf-host','newName':s['NOCTF_PLATFORM_IMAGE'].split('@')[0],
          'digest':s['NOCTF_PLATFORM_IMAGE'].split('@')[1]}],'patches':patches},indent=2)+'\n')
    keys={'jwt-secret':s['JWT_SECRET'],'db-password':s['POSTGRES_PASSWORD'],'seed-admin-password':s['SEED_ADMIN_PASSWORD'],
          'runner-scoring-key':s['RUNNER_SCORING_SECRET'],'email-verification-encryption-key':s['EMAIL_VERIFICATION_ENCRYPTION_KEY'],
          's3-access-key':s['S3_ACCESS_KEY'],'s3-secret-key':s['S3_SECRET_KEY'],'grafana-admin-password':s['GRAFANA_PASSWORD']}
    keys['postgres-connection']='Host=postgres;Port=5432;Database=noctf;Username=postgres;Password="'+s['POSTGRES_PASSWORD'].replace('"','""')+'"'
    secret={'apiVersion':'v1','kind':'Secret','metadata':{'name':'noctf-secrets','namespace':'noctf'},'type':'Opaque','stringData':keys}
    tls={'apiVersion':'v1','kind':'Secret','metadata':{'name':'noctf-tls','namespace':'noctf'},'type':'kubernetes.io/tls',
         'stringData':{'tls.crt':Path(s['tlsCert']).read_text(),'tls.key':Path(s['tlsKey']).read_text()}}
    objects=[secret,tls]
    if s.get('pullConfig'):
        content=json.loads(Path(s['pullConfig']).read_text())
        if not content.get('auths') or content.get('credsStore') or content.get('credHelpers'): raise ValueError('Registry config must contain inline auths, without desktop helpers.')
        objects.append({'apiVersion':'v1','kind':'Secret','metadata':{'name':'challenge-registry','namespace':'runtime'},'type':'kubernetes.io/dockerconfigjson','stringData':{'.dockerconfigjson':json.dumps(content)}})
        objects.append({'apiVersion':'v1','kind':'Secret','metadata':{'name':'host-registry','namespace':'noctf'},'type':'kubernetes.io/dockerconfigjson','stringData':{'.dockerconfigjson':json.dumps(content)}})
    write(stage/'secrets.json',json.dumps({'apiVersion':'v1','kind':'List','items':objects},indent=2)+'\n')

def generate(s):
    directory=validate(s); directory.mkdir(parents=True,exist_ok=True)
    if not (directory/'installation.json').exists(): directory.chmod(0o700)
    previous=load(directory)
    if previous and previous.get('storage','s3' if previous.get('target')=='kubernetes' else 'local') != s['storage']:
        raise ValueError('Storage changes require a separate quiesced migration; this wizard preserves the existing provider.')
    with tempfile.TemporaryDirectory(prefix='.noctf-stage-',dir=directory) as temp:
        stage=Path(temp)
        if s['target']=='docker': generate_docker(s,stage,directory)
        else: generate_kubernetes(s,stage,directory)
        write(stage/'installation.json',json.dumps(s,indent=2)+'\n')
        # Validate syntax before replacing any existing configuration file.
        check({**s,'directory':str(stage)})
        files=[path for path in stage.rglob('*') if path.is_file()]
        for file in files:
            destination=directory/file.relative_to(stage)
            if any(parent.is_symlink() for parent in [destination,*destination.parents] if parent!=Path('/')): raise ValueError('Configuration paths cannot traverse symlinks.')
        backup=directory/'.configuration-backups'/datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
        changed=[]
        try:
            for file in files:
                relative=file.relative_to(stage); destination=directory/relative
                destination.parent.mkdir(parents=True,exist_ok=True)
                if destination.exists():
                    old=backup/relative; old.parent.mkdir(parents=True,exist_ok=True); shutil.copy2(destination,old); old.chmod(0o600)
                changed.append(relative); os.replace(file,destination); destination.chmod(0o600)
        except Exception:
            for relative in reversed(changed):
                old=backup/relative; destination=directory/relative
                if old.exists(): os.replace(old,destination)
                elif destination.exists(): destination.unlink()
            raise
    if s['target']=='docker':
        for name,uid,gid in [('postgres',999,999),('redis',999,999),('nats',0,0),('registry',0,0),('uploads',0,0),('backups',0,0),('rustfs',10001,10001),('rustfs-logs',10001,10001),('observability/prometheus',65534,65534),('observability/grafana',472,472),('observability/loki',10001,10001)]:
            path=directory/'data'/name
            if not path.exists():
                path.mkdir(parents=True); path.chmod(0o750)
                if getattr(os,'geteuid',lambda:1)()==0: os.chown(path,uid,gid)
    check(s)
    print('Configuration generated and validated; secrets are stored in restricted files.')

def compose(s, monitoring=False):
    directory=Path(s['directory'])
    if monitoring:
        return ['docker','--context',s['context'],'compose','--project-directory',str(directory/'observability'),'--env-file',str(directory/'observability/.env'),'-f',str(directory/'observability/compose.yml')]
    args=['docker','--context',s['context'],'compose','--project-directory',str(directory),'--env-file',str(directory/'.env'),'-f',str(directory/'docker-compose.yml')]
    if s['storage']=='s3': args+=['-f',str(directory/'compose.rustfs.yml')]
    if s.get('monitoring')=='yes': args+=['-f',str(directory/'compose.monitoring.yml')]
    return args

def rendered(s):
    import yaml
    text=run(['kubectl','kustomize',str(Path(s['directory'])/'k8s/overlays/configured')],s)
    objects=list(yaml.safe_load_all(text)); config=next(x for x in objects if x.get('kind')=='ConfigMap' and x['metadata']['name']=='noctf-config')
    digest=hashlib.sha256(json.dumps(config['data'],sort_keys=True).encode()).hexdigest()
    for obj in objects:
        if obj.get('kind') in ('Deployment','StatefulSet') and obj['metadata']['name'] in ('backend','worker','runner'):
            obj['spec']['template']['metadata'].setdefault('annotations',{})['noctf.io/config-checksum']=digest
    return yaml.safe_dump_all(objects,sort_keys=False)

def check(s):
    validate(s)
    if s['target']=='docker':
        run(compose(s)+['config','--quiet'],s)
        if s.get('monitoring')=='yes': run(compose(s,True)+['config','--quiet'],s)
    else:
        text=rendered(s)
        if '.invalid' in text: raise ValueError('Replace all production placeholder origins.')
        write(Path(s['directory'])/'rendered.yaml',text)

def deploy(s):
    check(s); directory=Path(s['directory'])
    if s['target']=='docker':
        run(['docker','--context',s['context'],'network','inspect','1panel-network'],s)
        run(compose(s)+['up','-d','postgres','redis','nats','registry']+(['rustfs'] if s['storage']=='s3' else []),s)
        deadline=time.monotonic()+180
        while subprocess.run(compose(s)+['exec','-T','postgres','pg_isready','-U','noctf','-d','noctf'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL).returncode:
            if time.monotonic()>deadline: raise ValueError('PostgreSQL startup exceeded its budget.')
            time.sleep(2)
        if s['storage']=='s3': run(compose(s)+['run','--rm','--no-deps','storage-init'],s)
        if s.get('monitoring')=='yes':
            password=s.get('MONITOR_PASSWORD',s['GRAFANA_PASSWORD']).replace("'","''")
            bootstrap="DO $role$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname='noctf_monitor') THEN CREATE ROLE noctf_monitor LOGIN PASSWORD '"+password+"'; END IF; END $role$; GRANT pg_monitor TO noctf_monitor;"
            create=subprocess.run(compose(s)+['exec','-T','postgres','psql','-U',load(s['directory']).get('POSTGRES_USER','noctf'),'-d','postgres','-v','ON_ERROR_STOP=1'],input=bootstrap,text=True,stdout=subprocess.DEVNULL,stderr=subprocess.PIPE)
            if create.returncode: raise ValueError('Read-only exporter bootstrap failed; credentials were not printed.')
            run(compose(s,True)+['up','-d'],s)
        run(compose(s)+['up','-d','noctf'],s)
        deadline=time.monotonic()+300
        while True:
            result=subprocess.run(compose(s)+['exec','-T','noctf','/usr/local/bin/noctf-healthcheck'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
            if result.returncode==0: break
            if time.monotonic()>deadline: raise ValueError('Host readiness did not complete within 300 seconds.')
            time.sleep(2)
    else:
        k=['kubectl','--context',s['context']]
        cilium=json.loads(run(k+['-n','kube-system','get','configmap','cilium-config','-o','json'],s))
        if cilium['data'].get('enable-policy')!='always': raise ValueError('Cilium policy enforcement must be always.')
        run(k+['get','--raw','/apis/metrics.k8s.io/v1beta1/nodes'],s)
        nodes=json.loads(run(k+['get','nodes','-l','noctf.io/pod-pids-limit=256','-o','json'],s))['items']
        if not nodes: raise ValueError('No PID-attested Runtime nodes.')
        for node in nodes:
            config=json.loads(run(k+['get','--raw','/api/v1/nodes/'+node['metadata']['name']+'/proxy/configz'],s))
            if config['kubeletconfig']['podPidsLimit']!=256: raise ValueError('PID attestation does not match actual configuration.')
        storage=json.loads(run(k+['get','storageclass',s['storageClass'],'-o','json'],s)) if s['storageClass']!='noctf-retain' else None
        if storage and storage.get('reclaimPolicy')!='Retain': raise ValueError('Production storage class must retain its volumes.')
        first=not json.loads(run(k+['-n','noctf','get','deployment','backend','--ignore-not-found','-o','json'],s) or '{}')
        for file in ('namespace.yaml','00-runtime-namespace.yaml','networkpolicy.yaml','01-runtime-networkpolicy.yaml','runner-rbac.yaml'):
            run(k+['apply','-f',str(directory/'k8s/base'/file)],s)
        run(k+['apply','-f',str(directory/'secrets.json')],s)
        # First-install dependencies precede S3 initialization and Host migration.
        import yaml
        objects=list(yaml.safe_load_all((directory/'rendered.yaml').read_text()))
        for obj in objects:
            if first and obj.get('kind') in ('Deployment','StatefulSet') and obj['metadata']['name'] in ('backend','worker','runner'): obj['spec']['replicas']=0
        write(directory/'dependencies.yaml',yaml.safe_dump_all(objects,sort_keys=False))
        run(k+['apply','-f',str(directory/'dependencies.yaml')],s)
        for name in ('postgres','redis','nats','rustfs'):
            run(k+['-n','noctf','rollout','status','statefulset/'+name,'--timeout=300s'],s)
        if first:
            job=yaml.safe_load((directory/'k8s/init/base/storage-init-job.yaml').read_text())
            job['spec']['template']['spec']['containers'][0]['image']=s['NOCTF_PLATFORM_IMAGE']
            if s.get('pullConfig'): job['spec']['template']['spec']['imagePullSecrets']=[{'name':'host-registry'}]
            write(directory/'storage-init.yaml',yaml.safe_dump(job,sort_keys=False))
            run(k+['-n','noctf','delete','job','storage-init','--ignore-not-found'],s)
            run(k+['apply','-f',str(directory/'storage-init.yaml')],s)
            run(k+['-n','noctf','wait','--for=condition=complete','job/storage-init','--timeout=600s'],s)
        run(k+['apply','-f',str(directory/'rendered.yaml')],s)
        for resource in ('deployment/backend','deployment/worker','statefulset/runner'):
            run(k+['-n','noctf','rollout','status',resource,'--timeout=300s'],s)
    print('Deployment started and readiness checks passed. Existing data was retained.')

def main():
    parser=argparse.ArgumentParser(); parser.add_argument('command',choices=['get','generate','check','deploy','import-cluster']); parser.add_argument('path'); parser.add_argument('key',nargs='?')
    args=parser.parse_args()
    if args.command=='get': print(str(load(args.path).get(args.key,''))); return
    if args.command=='import-cluster':
        import base64
        context=args.key
        result=run(['kubectl','--context',context,'-n','noctf','get','secret','noctf-secrets','--ignore-not-found','-o','json'],{})
        if not result.strip(): return
        secret=json.loads(result)
        mapping={'jwt-secret':'JWT_SECRET','db-password':'POSTGRES_PASSWORD','seed-admin-password':'SEED_ADMIN_PASSWORD','runner-scoring-key':'RUNNER_SCORING_SECRET','email-verification-encryption-key':'EMAIL_VERIFICATION_ENCRYPTION_KEY','s3-access-key':'S3_ACCESS_KEY','s3-secret-key':'S3_SECRET_KEY','grafana-admin-password':'GRAFANA_PASSWORD'}
        values={'target':'kubernetes','storage':'s3','context':context}
        for key,name in mapping.items():
            if key in secret['data']: values[name]=base64.b64decode(secret['data'][key]).decode()
        path=Path(args.path); path.mkdir(parents=True,exist_ok=True); path.chmod(0o700)
        write(path/'installation.json',json.dumps(values,indent=2)+'\n')
        print('Existing cluster secrets imported into restricted configuration; values are not displayed.')
        return
    if args.command=='generate':
        raw=sys.stdin.buffer.read().split(b'\0'); settings={raw[i].decode():raw[i+1].decode() for i in range(0,len(raw)-1,2)}
        generate(settings)
    else:
        settings=load(args.path)
        try: (check if args.command=='check' else deploy)(settings)
        except Exception:
            if args.command=='deploy': diagnose(settings)
            raise

if __name__=='__main__':
    try: main()
    except (ValueError,KeyError,OSError,json.JSONDecodeError) as error:
        print('Configuration error: '+str(error),file=sys.stderr); sys.exit(1)
