"""Real offline inspection CLI. Fixtures are generated PE/metadata, never games."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[1]
CLI=ROOT/'controller/UGEVR.Controller/bin/Release/net10.0/UGEVR.Controller.dll'
POWERSHELL=Path(os.environ.get('UGEVR_TEST_PWSH',str(Path.home()/'.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe')))

def native(machine=0x8664,exports=(),version=None,dll=False):
    """Minimal valid data-only PE fixture, with export names and optional RT_VERSION.
    Export addresses are backed bytes, not callable game implementations.
    """
    b=bytearray(0x2200);b[:2]=b'MZ';struct.pack_into('<I',b,60,0x80);b[0x80:0x84]=b'PE\0\0'
    plus=machine!=0x14c;optional=240 if plus else 224
    struct.pack_into('<HHIIIHH',b,0x84,machine,1,0,0,0,optional,0x22|(0x2000 if dll else 0))
    o=0x98;struct.pack_into('<H',b,o,0x20b if plus else 0x10b)
    struct.pack_into('<I',b,o+16,0x1000);struct.pack_into('<II',b,o+32,0x1000,0x200)
    struct.pack_into('<II',b,o+56,0x4000,0x200);struct.pack_into('<H',b,o+68,3)
    struct.pack_into('<I',b,o+(108 if plus else 92),16);directories=o+(112 if plus else 96)
    section=o+optional;b[section:section+8]=b'.rdata\0\0'
    struct.pack_into('<IIII',b,section+8,0x2000,0x1000,0x2000,0x200);struct.pack_into('<I',b,section+36,0x60000040)
    def at(rva):return rva-0x1000+0x200
    if exports:
        struct.pack_into('<II',b,directories,0x1100,0x700)
        struct.pack_into('<IIIIII',b,at(0x1100)+16,1,len(exports),len(exports),0x1180,0x1200,0x1280)
        cursor=0x1300
        for i,name in enumerate(exports):
            struct.pack_into('<I',b,at(0x1180)+4*i,0x1000+i)
            struct.pack_into('<I',b,at(0x1200)+4*i,cursor);struct.pack_into('<H',b,at(0x1280)+2*i,i)
            data=name.encode()+b'\0';b[at(cursor):at(cursor)+len(data)]=data;cursor+=len(data)
    if version:
        struct.pack_into('<II',b,directories+16,0x1800,0x200)
        for offset,name,next_offset in [(0,16,0x20),(0x20,1,0x40)]:
            struct.pack_into('<H',b,at(0x1800)+offset+14,1)
            struct.pack_into('<II',b,at(0x1800)+offset+16,name,0x80000000|next_offset)
        struct.pack_into('<H',b,at(0x1840)+14,1);struct.pack_into('<II',b,at(0x1840)+16,0x409,0x60)
        struct.pack_into('<IIII',b,at(0x1860),0x1900,92,0,0)
        v=at(0x1900);struct.pack_into('<HHH',b,v,92,52,0);key='VS_VERSION_INFO\0'.encode('utf-16le');b[v+6:v+6+len(key)]=key
        major,minor,patch,revision=version;ms=major<<16|minor;ls=patch<<16|revision
        struct.pack_into('<13I',b,v+40,0xfeef04bd,0x10000,ms,ls,ms,ls,0,0,0,0,0,0,0)
    return bytes(b)

def write(path,data):path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(data)

class Inspect(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.generated=tempfile.TemporaryDirectory(prefix='ugevr-inspect-metadata-');base=Path(cls.generated.name)
        cls.managed={}
        definitions=[('core','UnityEngine.CoreModule','2021.3.12.0','2021.3.12f1','public class EngineMarker {}'),
                     ('conflict','UnityEngine','2022.3.1.0','2022.3.1f1','public class OtherEngineMarker {}'),
                     ('pipeline','Unity.RenderPipelines.Universal.Runtime','12.1.0.0','12.1.0','public class PipelineMarker {}'),
                     ('bep','BepInEx','5.4.0.0','5.4.0','public class LoaderMarker {}'),
                     ('melon','MelonLoader','1.0.0.0','1.0.0','public class OtherLoaderMarker {}')]
        for folder,name,version,info,code in definitions:
            directory=base/folder;directory.mkdir()
            (directory/'fixture.csproj').write_text(f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>{name}</AssemblyName><FileVersion>{version}</FileVersion><InformationalVersion>{info}</InformationalVersion><IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion></PropertyGroup></Project>')
            (directory/'Fixture.cs').write_text(code)
            p=subprocess.run(['dotnet','build',str(directory/'fixture.csproj'),'-c','Release','--nologo'],capture_output=True,text=True,timeout=40)
            if p.returncode:raise AssertionError(p.stdout+p.stderr)
            cls.managed[folder]=directory/'bin/Release/net10.0'/f'{name}.dll'
        directory=base/'reference';directory.mkdir()
        hint=cls.managed['pipeline'].as_posix()
        (directory/'fixture.csproj').write_text(f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>RetainedReference</AssemblyName></PropertyGroup><ItemGroup><Reference Include="Unity.RenderPipelines.Universal.Runtime"><HintPath>{hint}</HintPath></Reference></ItemGroup></Project>')
        (directory/'Fixture.cs').write_text('public class Consumer { public PipelineMarker Value = new PipelineMarker(); }')
        result=subprocess.run(['dotnet','build',str(directory/'fixture.csproj'),'-c','Release','--nologo'],capture_output=True,text=True,timeout=40)
        if result.returncode:raise AssertionError(result.stdout+result.stderr)
        cls.managed['reference']=directory/'bin/Release/net10.0/RetainedReference.dll'
    @classmethod
    def tearDownClass(cls):cls.generated.cleanup()
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='ugevr-inspect-check-');self.scope=Path(self.temp.name)
        self.root=self.scope/'install';self.root.mkdir();self.exe=self.root/'Game.exe';self.data=self.root/'Game_Data';self.data.mkdir();write(self.exe,native())
    def tearDown(self):self.temp.cleanup()
    def cli(self,*args,expected=0,exe=None,env=None):
        p=subprocess.run(['dotnet',str(CLI),'inspect','--game-exe',str(exe or self.exe),'--format','json',*map(str,args)],capture_output=True,text=True,timeout=15,env=env)
        self.assertEqual(p.stderr,'');self.assertEqual(p.returncode,expected,p.stdout)
        report=json.loads(p.stdout);self.assertEqual(report['schema_version'],1)
        self.assertIs(report['runtime_support_established'],False);self.assertIs(report['tested_compatibility_established'],False);self.assertIs(report['writes_performed'],False)
        self.assertLessEqual(len(p.stdout.encode()),524289);return report
    def artifact(self,report,name):return next(a for a in report['artifacts'] if a['path']==name)
    def core(self,variant='core',filename=None):
        source=self.managed[variant];write(self.data/'Managed'/(filename or source.name),source.read_bytes())
    def mono(self,machine=0x8664):write(self.root/'MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll',native(machine,exports=['mono_get_root_domain','mono_runtime_invoke'],dll=True))
    def il2cpp(self):
        write(self.root/'GameAssembly.dll',native(exports=['il2cpp_domain_get','il2cpp_runtime_invoke'],dll=True))
        write(self.data/'il2cpp_data/Metadata/global-metadata.dat',struct.pack('<II',0xfab11baf,24))
    def test_pe_architectures_are_file_facts_never_support(self):
        for machine,name in [(0x8664,'x64'),(0x14c,'x86'),(0xaa64,'arm64'),(0x9999,'other_or_unknown')]:
            with self.subTest(machine=machine):
                write(self.exe,native(machine));r=self.cli();self.assertEqual(r['selected_executable']['pe']['machine'],name)
                self.assertEqual(r['unity']['interpretation'],'unconfirmed');self.assertEqual(r['active_graphics_api'],'unknown')
    def test_non_pe_truncation_offsets_and_size_refuse_bounded(self):
        cases=[b'not Unity',b'MZ'+b'\0'*62,native()[:150]]
        corrupt=bytearray(native());struct.pack_into('<I',corrupt,60,0xffffffff);cases.append(corrupt)
        for content in cases:
            write(self.exe,content);r=self.cli(expected=2);self.assertIn(r['selected_executable']['state'],['refused','malformed'])
        with self.exe.open('wb') as stream:stream.truncate(134217729)
        r=self.cli(expected=2);self.assertEqual(r['selected_executable']['reason_code'],'artifact_size_exceeded')
    def test_filename_only_backend_decoys_remain_unknown(self):
        write(self.root/'GameAssembly.dll',native(dll=True));write(self.root/'UnityPlayer.dll',native(version=(2021,3,12,0),dll=True))
        write(self.data/'il2cpp_data/Metadata/global-metadata.dat',b'not metadata');self.core(filename='Renamed.dll')
        write(self.root/'MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll',native(dll=True))
        r=self.cli();self.assertEqual(r['backend']['interpretation'],'unknown')
        self.assertFalse(r['backend']['mono_export_evidence']);self.assertFalse(r['backend']['il2_cpp_export_and_header_evidence'])
        self.assertFalse(any(v['artifact']=='UnityPlayer.dll' for v in r['unity']['version_evidence']))
    def test_content_backed_mono_and_exact_metadata_identity(self):
        self.core(filename='ArbitraryName.dll');self.mono();r=self.cli()
        self.assertEqual(r['backend']['interpretation'],'mono_content_candidate');self.assertEqual(r['unity']['interpretation'],'unity_content_candidate')
        a=self.artifact(r,'Game_Data/Managed/ArbitraryName.dll');self.assertEqual(a['pe']['managed']['assembly_name'],'UnityEngine.CoreModule');self.assertEqual(len(a['sha256']),64)
    def test_content_backed_il2cpp_and_mixed_backend_ambiguity(self):
        self.il2cpp();r=self.cli();self.assertEqual(r['backend']['interpretation'],'il2cpp_content_candidate')
        self.assertEqual(self.artifact(r,'Game_Data/il2cpp_data/Metadata/global-metadata.dat')['il2_cpp_metadata_version'],24)
        self.mono();r=self.cli();self.assertEqual(r['backend']['interpretation'],'ambiguous_mono_and_il2cpp_content')
    def test_architecture_disagreement_remains_ambiguous(self):
        self.mono(0x14c);r=self.cli();self.assertEqual(r['backend']['interpretation'],'ambiguous_architecture_evidence')
    def test_managed_and_native_version_sources_disagreement(self):
        self.core();write(self.root/'UnityPlayer.dll',native(exports=['UnityMain'],version=(2021,3,12,0),dll=True))
        r=self.cli();self.assertEqual(r['unity']['version_family'],'2021.3.12')
        self.assertEqual({v['source'] for v in r['unity']['version_evidence']},{'managed_version_attribute','pe_fixed_product_version'})
        self.core('conflict');r=self.cli();self.assertEqual(r['unity']['version_state'],'conflicting_version_evidence');self.assertIsNone(r['unity']['version_family'])
    def test_pipeline_artifact_is_not_active_pipeline(self):
        self.core('pipeline');r=self.cli();self.assertEqual(r['pipeline']['active_pipeline'],'unknown')
        self.assertEqual(r['pipeline']['retained_artifacts'][0]['identity'],'Unity.RenderPipelines.Universal.Runtime');self.assertFalse(r['pipeline']['absence_proves_built_in'])
    def test_pipeline_reference_without_package_does_not_prove_active_pipeline(self):
        self.core('reference');r=self.cli()
        retained=r['pipeline']['retained_artifacts'];self.assertEqual(len(retained),1)
        self.assertEqual(retained[0]['identity'],'RetainedReference')
        self.assertEqual(retained[0]['references'],['Unity.RenderPipelines.Universal.Runtime, Version=1.0.0.0'])
        self.assertEqual(r['pipeline']['active_pipeline'],'unknown')
    def test_loader_content_conflict_has_no_automatic_remedy(self):
        write(self.root/'BepInEx/core/BepInEx.dll',self.managed['bep'].read_bytes());write(self.root/'MelonLoader/MelonLoader.dll',self.managed['melon'].read_bytes())
        r=self.cli();self.assertEqual(r['loader']['interpretation'],'potential_loader_conflict');self.assertEqual(r['loader']['compatibility'],'unknown')
    def test_export_ordinal_and_resource_offsets_refuse(self):
        b=bytearray(native(exports=['mono_get_root_domain'],dll=True));struct.pack_into('<H',b,0x480,12)
        write(self.root/'MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll',b);r=self.cli()
        self.assertEqual(self.artifact(r,'MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll')['reason_code'],'export_ordinal_out_of_range');self.assertEqual(r['backend']['interpretation'],'unknown')
        b=bytearray(native(version=(2021,3,12,0),dll=True));struct.pack_into('<I',b,0xa14,0x8000ffff)
        write(self.root/'UnityPlayer.dll',b);r=self.cli();self.assertEqual(self.artifact(r,'UnityPlayer.dll')['reason_code'],'resource_offset_out_of_range')
    def test_zero_fill_exported_data_and_placeholder_version(self):
        # Valid native export tables can contain unrelated data in virtual-only BSS.
        b=bytearray(native(exports=['mono_get_root_domain','mono_runtime_invoke','mono_data'],dll=True))
        struct.pack_into('<I',b,0x200+0x180+8,0x3500)
        section=0x98+240;struct.pack_into('<I',b,section+8,0x3000)
        write(self.root/'MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll',b)
        write(self.root/'UnityPlayer.dll',native(exports=['UnityMain'],version=(0,0,0,0),dll=True))
        self.core();r=self.cli();self.assertEqual(r['backend']['interpretation'],'mono_content_candidate')
        self.assertEqual(r['unity']['version_family'],'2021.3.12');self.assertEqual(r['unity']['version_state'],'version_family_evidence')
        zero=next(v for v in r['unity']['version_evidence'] if v['value']=='0.0.0.0');self.assertIsNone(zero['family'])
        struct.pack_into('<I',b,0x200+0x180,0x3500)
        write(self.root/'MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll',b)
        r=self.cli();self.assertEqual(r['backend']['interpretation'],'unknown')
    def test_data_adjacent_embedded_mono_location(self):
        write(self.data/'Mono/EmbedRuntime/mono.dll',native(exports=['mono_get_root_domain','mono_runtime_invoke'],dll=True))
        self.assertEqual(self.cli()['backend']['interpretation'],'mono_content_candidate')
    def test_bounded_artifact_inventory_is_never_complete_subset(self):
        for i in range(260):write(self.data/'Managed'/f'a{i:03}.dll',native(dll=True))
        r=self.cli(expected=2);self.assertFalse(r['discovery_complete']);self.assertIn('artifact_count_exceeded',r['issues']);self.assertLessEqual(r['scope']['files_attempted'],256)
        self.assertEqual(r['backend']['interpretation'],'unknown_incomplete_inventory')
    def test_directory_count_bound_and_no_implicit_other_data_selection(self):
        self.data.rmdir();(self.root/'Other_Data').mkdir();r=self.cli();self.assertIsNone(r['scope']['data_directory']);self.assertIn('Other_Data',r['scope']['adjacent_data_candidates'])
        for i in range(1025):write(self.root/f'entry{i:04}.txt',b'')
        r=self.cli(expected=2);self.assertIn('directory_entry_count_exceeded',r['issues']);self.assertFalse(r['discovery_complete'])
    def test_output_read_only_hashes_and_no_target_execution(self):
        self.core();self.mono();before={str(p.relative_to(self.root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in self.root.rglob('*') if p.is_file()}
        r=self.cli();after={str(p.relative_to(self.root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in self.root.rglob('*') if p.is_file()};self.assertEqual(before,after)
        self.assertEqual(r['vr_runtime_state'],'unobserved');self.assertEqual(r['backend']['callable_implementations'],'unobserved')
        source=(ROOT/'controller/UGEVR.Controller/PlayerInspection.cs').read_text();self.assertNotRegex(source,r'(Assembly\.(Load|LoadFrom|LoadFile)|Process\.Start|NativeLibrary\.Load)\s*\(')
    def test_default_yaml_matches_json_structure(self):
        try:import yaml
        except ImportError:self.skipTest('PyYAML is only a development check dependency; shipping has none')
        expected=self.cli();p=subprocess.run(['dotnet',str(CLI),'inspect','--game-exe',str(self.exe)],capture_output=True,text=True,timeout=15)
        self.assertEqual(p.returncode,0);self.assertEqual(yaml.safe_load(p.stdout),expected)
    def test_invalid_arguments_are_versioned_structured_refusal(self):
        r=self.cli(exe=Path('relative.exe'),expected=2);self.assertEqual(r['reason_code'],'absolute_game_executable_required')
        p=subprocess.run(['dotnet',str(CLI),'inspect','--game-exe',str(self.exe),'--format','bad'],capture_output=True,text=True,timeout=15)
        self.assertEqual(p.returncode,2);self.assertIn('unsupported_output_format',p.stdout)
    @unittest.skipUnless(os.name=='nt','actual NTFS ACL failure')
    def test_actual_file_and_directory_access_denial(self):
        self.core();target=self.data/'Managed/UnityEngine.CoreModule.dll'
        helper=ROOT/'tests/inspect_acl.ps1';sddl=self.scope/'original.sddl'
        for path in (target,target.parent):
            p=subprocess.run([str(POWERSHELL),'-NoProfile','-NonInteractive','-File',str(helper),'deny',str(self.scope),str(path),str(sddl)],capture_output=True,text=True,timeout=10);self.assertEqual(p.returncode,0,p.stderr)
            try:
                r=self.cli(expected=0 if path==target else 2)
                if path==target:self.assertEqual(self.artifact(r,'Game_Data/Managed/UnityEngine.CoreModule.dll')['state'],'inaccessible');self.assertFalse(r['evidence_complete'])
                else:self.assertFalse(r['discovery_complete']);self.assertIn('directory_access_denied',r['issues'])
            finally:
                p=subprocess.run([str(POWERSHELL),'-NoProfile','-NonInteractive','-File',str(helper),'restore',str(self.scope),str(path),str(sddl)],capture_output=True,text=True,timeout=10);self.assertEqual(p.returncode,0,p.stderr)
        self.assertEqual(target.read_bytes(),self.managed['core'].read_bytes())
    @unittest.skipUnless(os.name=='nt','Windows junction refusal')
    def test_reparse_directory_not_followed(self):
        self.data.rmdir();outside=self.scope/'outside';outside.mkdir();write(outside/'Managed/UnityEngine.CoreModule.dll',self.managed['core'].read_bytes())
        p=subprocess.run(['cmd','/c','mklink','/J',str(self.data),str(outside)],capture_output=True,text=True,timeout=5);self.assertEqual(p.returncode,0,p.stderr)
        try:
            r=self.cli(expected=2);self.assertIn('reparse_path_refused',r['issues']);self.assertFalse(r['discovery_complete']);self.assertFalse(any(a['role']=='managed_inventory' for a in r['artifacts']))
        finally:os.rmdir(self.data)
        self.assertEqual((outside/'Managed/UnityEngine.CoreModule.dll').read_bytes(),self.managed['core'].read_bytes())

if __name__=='__main__':unittest.main(verbosity=2)
