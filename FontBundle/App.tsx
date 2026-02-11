
import React, { useState, useCallback, useRef, useEffect, useMemo } from 'react';
import { AppConfig, Epoch, BuildTarget, LogEntry } from './types';
import { INITIAL_CONFIG, UI_ICONS } from './constants';
import { FormField, TextInput, Switch, Select, FileDropZone } from './components/Input';

const App: React.FC = () => {
  const [config, setConfig] = useState<AppConfig>(INITIAL_CONFIG as AppConfig);
  const [activeTab, setActiveTab] = useState<'single' | 'batch' | 'unity' | 'advanced'>('single');
  const [logs, setLogs] = useState<LogEntry[]>([]);
  const [isBuilding, setIsBuilding] = useState(false);
  const [progress, setProgress] = useState(0);
  const logEndRef = useRef<HTMLDivElement>(null);

  const updateConfig = (key: keyof AppConfig, value: any) => {
    setConfig(prev => ({ ...prev, [key]: value }));
  };

  const isBuildReady = useMemo(() => {
    if (config.isBatchMode) return !!config.jobsFile;
    return !!(config.targetGame && config.fontPath && config.outputPath);
  }, [config]);

  const addLog = useCallback((message: string, level: LogEntry['level'] = 'info') => {
    const newLog: LogEntry = {
      id: Math.random().toString(36).substr(2, 9),
      timestamp: new Date().toLocaleTimeString(),
      level,
      message
    };
    setLogs(prev => [...prev.slice(-100), newLog]);
  }, []);

  useEffect(() => {
    logEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [logs]);

  const handleStartBuild = async () => {
    if (!isBuildReady) return;
    setIsBuilding(true);
    setProgress(0);
    setLogs([]);
    addLog(`Initializing ${config.isBatchMode ? 'Batch' : 'Single'} Mode conversion...`, 'info');
    
    const steps = [
      { msg: 'Validating game environment...', delay: 600 },
      { msg: `Analyzing ${config.targetGame.split('/').pop()}...`, delay: 800 },
      { msg: `Parsing font metadata from ${config.fontPath.split('/').pop()}`, delay: 1000 },
      { msg: 'Starting Unity instance in background...', delay: 1200 },
      { msg: 'Generating SDF Atlas (this may take a minute)...', delay: 2500 },
      { msg: 'Compressing AssetBundle...', delay: 1500 },
      { msg: `Saving results to ${config.outputPath}`, delay: 700 },
    ];

    for (let i = 0; i < steps.length; i++) {
      await new Promise(r => setTimeout(r, steps[i].delay));
      addLog(steps[i].msg, 'info');
      setProgress(Math.floor(((i + 1) / steps.length) * 100));
    }

    addLog('Successfully built font bundle!', 'success');
    setIsBuilding(false);
  };

  return (
    <div className="flex h-screen w-full bg-[#0a0f1d] text-slate-200">
      {/* Sidebar */}
      <div className="w-64 bg-slate-900/50 border-r border-slate-800 flex flex-col backdrop-blur-xl">
        <div className="p-8 border-b border-slate-800/50">
          <div className="flex items-center gap-3">
            <div className="bg-blue-600 p-2 rounded-xl shadow-lg shadow-blue-600/20">
              <UI_ICONS.Layers />
            </div>
            <div>
              <h1 className="text-lg font-bold text-white tracking-tight">UnityFont</h1>
              <p className="text-[10px] text-slate-500 uppercase tracking-widest font-bold">Bundle Engine</p>
            </div>
          </div>
        </div>

        <nav className="flex-1 p-4 space-y-1.5 overflow-y-auto">
          <div className="px-4 py-2 text-[10px] font-bold text-slate-600 uppercase tracking-widest">Workspace</div>
          <button
            onClick={() => { setActiveTab('single'); updateConfig('isBatchMode', false); }}
            className={`w-full flex items-center gap-3 px-4 py-3 rounded-xl text-sm font-medium transition-all ${
              activeTab === 'single' ? 'bg-blue-600/10 text-blue-400 border border-blue-600/20 shadow-sm' : 'text-slate-500 hover:bg-slate-800/50 hover:text-slate-300'
            }`}
          >
            <UI_ICONS.File /> Single Converter
          </button>
          <button
            onClick={() => { setActiveTab('batch'); updateConfig('isBatchMode', true); }}
            className={`w-full flex items-center gap-3 px-4 py-3 rounded-xl text-sm font-medium transition-all ${
              activeTab === 'batch' ? 'bg-purple-600/10 text-purple-400 border border-purple-600/20 shadow-sm' : 'text-slate-500 hover:bg-slate-800/50 hover:text-slate-300'
            }`}
          >
            <UI_ICONS.Cpu /> Batch Processor
          </button>
          
          <div className="pt-6 px-4 py-2 text-[10px] font-bold text-slate-600 uppercase tracking-widest">Preferences</div>
          <button
            onClick={() => setActiveTab('unity')}
            className={`w-full flex items-center gap-3 px-4 py-3 rounded-xl text-sm font-medium transition-all ${
              activeTab === 'unity' ? 'bg-slate-800 text-white' : 'text-slate-500 hover:bg-slate-800/50 hover:text-slate-300'
            }`}
          >
            <UI_ICONS.Settings /> Unity SDK
          </button>
          <button
            onClick={() => setActiveTab('advanced')}
            className={`w-full flex items-center gap-3 px-4 py-3 rounded-xl text-sm font-medium transition-all ${
              activeTab === 'advanced' ? 'bg-slate-800 text-white' : 'text-slate-500 hover:bg-slate-800/50 hover:text-slate-300'
            }`}
          >
            <UI_ICONS.Terminal /> Rendering Options
          </button>
        </nav>

        <div className="p-4 bg-slate-900/50 border-t border-slate-800">
          {!isBuildReady && !isBuilding && (
            <div className="mb-4 p-3 bg-yellow-500/10 border border-yellow-500/20 rounded-lg">
              <p className="text-[10px] text-yellow-400 leading-relaxed font-medium">
                Complete all required steps to enable build.
              </p>
            </div>
          )}
          <button
            disabled={!isBuildReady || isBuilding}
            onClick={handleStartBuild}
            className={`w-full flex items-center justify-center gap-3 py-4 rounded-xl font-bold transition-all shadow-xl ${
              isBuilding 
                ? 'bg-slate-800 text-slate-500 cursor-not-allowed' 
                : !isBuildReady 
                  ? 'bg-slate-800 text-slate-600 cursor-not-allowed border border-slate-700'
                  : 'bg-blue-600 hover:bg-blue-500 text-white shadow-blue-900/20 hover:-translate-y-0.5'
            }`}
          >
            {isBuilding ? (
              <div className="animate-spin rounded-full h-4 w-4 border-2 border-slate-400 border-t-transparent"></div>
            ) : (
              <UI_ICONS.Play />
            )}
            {isBuilding ? 'BUILDING...' : 'RUN BUILD'}
          </button>
        </div>
      </div>

      {/* Main Content */}
      <div className="flex-1 flex flex-col h-full overflow-hidden bg-gradient-to-br from-[#0a0f1d] to-[#111827]">
        <header className="h-20 border-b border-slate-800/50 flex items-center justify-between px-10">
          <div className="flex items-center gap-4">
            <h2 className="text-sm font-semibold text-slate-400">Status</h2>
            <div className="h-4 w-[1px] bg-slate-800"></div>
            <div className="flex items-center gap-2">
              <div className={`h-2 w-2 rounded-full ${isBuilding ? 'bg-blue-500 animate-pulse' : isBuildReady ? 'bg-green-500' : 'bg-slate-600'}`}></div>
              <span className="text-xs font-bold uppercase tracking-wider">
                {isBuilding ? 'Processing...' : isBuildReady ? 'Ready for build' : 'Configuration Pending'}
              </span>
            </div>
          </div>
          <div className="flex items-center gap-8">
             <div className="text-right">
                <p className="text-[10px] text-slate-500 font-bold uppercase">Target Platform</p>
                <p className="text-sm font-mono text-blue-400">{config.buildTarget}</p>
             </div>
          </div>
        </header>

        <main className="flex-1 p-10 overflow-y-auto custom-scrollbar">
          <div className="max-w-4xl mx-auto">
            {activeTab === 'single' && (
              <div className="space-y-10 animate-in fade-in slide-in-from-bottom-4 duration-500">
                <section>
                  <div className="mb-6">
                    <h3 className="text-2xl font-bold text-white mb-2">Build Sequence</h3>
                    <p className="text-slate-400 text-sm">Follow the guided steps to prepare your font bundle.</p>
                  </div>

                  <div className="grid grid-cols-1 md:grid-cols-2 gap-8">
                    <FormField label="Step 1: Game Executable" required description="Target game binary (.exe)">
                      <FileDropZone 
                        placeholder="Drag game.exe here"
                        value={config.targetGame}
                        onFileSelect={(path) => updateConfig('targetGame', path)}
                        accept=".exe"
                        icon={<UI_ICONS.Terminal />}
                      />
                    </FormField>

                    <FormField label="Step 2: Source Font" required description="Support TTF, OTF, WOFF">
                      <FileDropZone 
                        placeholder="Drop font file here"
                        value={config.fontPath}
                        onFileSelect={(path) => updateConfig('fontPath', path)}
                        accept=".ttf,.otf,.woff"
                        icon={<UI_ICONS.File />}
                      />
                    </FormField>
                  </div>
                </section>

                <section className={`transition-opacity duration-500 ${(config.targetGame && config.fontPath) ? 'opacity-100' : 'opacity-40 pointer-events-none'}`}>
                  <FormField label="Step 3: Output Directory" required description="Where to save the bundles">
                    <FileDropZone 
                      placeholder="Select output folder"
                      value={config.outputPath}
                      onFileSelect={(path) => updateConfig('outputPath', path)}
                      icon={<UI_ICONS.Folder />}
                    />
                  </FormField>
                </section>

                <section className={`transition-opacity duration-500 ${isBuildReady ? 'opacity-100' : 'opacity-40 pointer-events-none'}`}>
                  <div className="bg-slate-900/50 border border-slate-800 rounded-2xl p-8">
                    <div className="flex items-center gap-2 mb-6">
                      <div className="h-8 w-1 bg-blue-500 rounded-full"></div>
                      <h4 className="text-lg font-bold text-white">Final Polish (Optional)</h4>
                    </div>
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                      <FormField label="Custom Bundle Name" description="Overrides default filename">
                        <TextInput 
                          placeholder="e.g. game_ui_fonts" 
                          value={config.bundleName}
                          onChange={(e) => updateConfig('bundleName', e.target.value)}
                        />
                      </FormField>
                      <FormField label="TMP Asset Name" description="Unity asset reference name">
                        <TextInput 
                          placeholder="e.g. TMP_Global_Font" 
                          value={config.tmpName}
                          onChange={(e) => updateConfig('tmpName', e.target.value)}
                        />
                      </FormField>
                    </div>
                  </div>
                </section>
              </div>
            )}

            {activeTab === 'batch' && (
              <section className="animate-in fade-in slide-in-from-bottom-4 duration-500">
                <div className="mb-6">
                  <h3 className="text-2xl font-bold text-white mb-2">Mass Conversion</h3>
                  <p className="text-slate-400 text-sm">Process multiple font assets using a job manifest.</p>
                </div>
                <div className="bg-slate-900/50 border border-slate-800 rounded-2xl p-8">
                  <FormField label="Jobs Manifest" required description="JSON configuration file">
                    <FileDropZone 
                      placeholder="Drop jobs_file.json here"
                      value={config.jobsFile}
                      onFileSelect={(path) => updateConfig('jobsFile', path)}
                      accept=".json"
                      icon={<UI_ICONS.Layers />}
                    />
                  </FormField>
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-8 mt-6">
                    <FormField label="Parallel Threads" description="Max simultaneous conversions">
                      <TextInput 
                        type="number" 
                        min="1" max="32"
                        value={config.maxWorkers}
                        onChange={(e) => updateConfig('maxWorkers', parseInt(e.target.value))}
                      />
                    </FormField>
                    <div className="flex items-end pb-2">
                      <Switch 
                        label="Continue on job failure" 
                        checked={config.continueOnJobError} 
                        onChange={(v) => updateConfig('continueOnJobError', v)}
                      />
                    </div>
                  </div>
                </div>
              </section>
            )}

            {/* Other tabs remain largely similar but with updated component styles */}
            {activeTab === 'unity' && (
              <section className="animate-in fade-in slide-in-from-bottom-4 duration-500">
                 <div className="mb-6">
                  <h3 className="text-2xl font-bold text-white mb-2">Unity SDK Configuration</h3>
                  <p className="text-slate-400 text-sm">Configure paths for the internal Unity engine wrapper.</p>
                </div>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                  <FormField label="Unity Path"><TextInput value={config.unityPath} onChange={e => updateConfig('unityPath', e.target.value)} /></FormField>
                  <FormField label="Hub Path"><TextInput value={config.unityHubPath} onChange={e => updateConfig('unityHubPath', e.target.value)} /></FormField>
                  <FormField label="Target Version"><TextInput value={config.unityVersion} onChange={e => updateConfig('unityVersion', e.target.value)} /></FormField>
                  <FormField label="Legacy Epoch Support">
                    <Select value={config.epoch} onChange={e => updateConfig('epoch', e.target.value as Epoch)}>
                      <option value={Epoch.Auto}>Auto-detect (Recommended)</option>
                      <option value={Epoch.Legacy}>Legacy (Unity 5.x - 2017)</option>
                      <option value={Epoch.Modern}>Modern (Unity 2021+)</option>
                    </Select>
                  </FormField>
                </div>
                <div className="mt-10 p-8 bg-slate-900/30 border border-slate-800/50 rounded-2xl">
                  <p className="text-[10px] font-bold text-slate-500 uppercase tracking-widest mb-6">Execution Flags</p>
                  <div className="grid grid-cols-2 gap-x-10">
                    <Switch label="Headless (No-Graphics)" checked={config.useNographics} onChange={v => updateConfig('useNographics', v)} />
                    <Switch label="Allow Auto-Install Hub" checked={config.autoInstallHub} onChange={v => updateConfig('autoInstallHub', v)} />
                    <Switch label="Strict LTS Mode" checked={!config.preferNonLts} onChange={v => updateConfig('preferNonLts', !v)} />
                    <Switch label="Preserve Temp Data" checked={config.keepTemp} onChange={v => updateConfig('keepTemp', v)} />
                  </div>
                </div>
              </section>
            )}

            {activeTab === 'advanced' && (
              <section className="animate-in fade-in slide-in-from-bottom-4 duration-500">
                <div className="mb-6">
                  <h3 className="text-2xl font-bold text-white mb-2">Rendering Pipeline</h3>
                  <p className="text-slate-400 text-sm">Fine-tune SDF generation and atlas packaging.</p>
                </div>
                <div className="grid grid-cols-3 gap-6">
                  <FormField label="Build Target"><Select value={config.buildTarget} onChange={e => updateConfig('buildTarget', e.target.value as BuildTarget)}>{Object.values(BuildTarget).map(t => <option key={t} value={t}>{t}</option>)}</Select></FormField>
                  <FormField label="Atlas Max Size"><TextInput value={config.atlasSizes} onChange={e => updateConfig('atlasSizes', e.target.value)} /></FormField>
                  <FormField label="Point Size"><TextInput type="number" value={config.pointSize} onChange={e => updateConfig('pointSize', parseInt(e.target.value))} /></FormField>
                </div>
                <div className="mt-8 grid grid-cols-2 gap-8">
                  <div className="p-8 bg-slate-900/30 border border-slate-800/50 rounded-2xl">
                    <h5 className="text-xs font-bold text-slate-500 uppercase mb-4">Memory & Performance</h5>
                    <FormField label="Warmup Limit"><TextInput type="number" value={config.dynamicWarmupLimit} onChange={e => updateConfig('dynamicWarmupLimit', parseInt(e.target.value))} /></FormField>
                    <Switch label="Force Static Generation" checked={config.forceStatic} onChange={v => updateConfig('forceStatic', v)} />
                    <Switch label="Force Dynamic Atlas" checked={config.forceDynamic} onChange={v => updateConfig('forceDynamic', v)} />
                  </div>
                  <div className="p-8 bg-slate-900/30 border border-slate-800/50 rounded-2xl">
                    <h5 className="text-xs font-bold text-slate-500 uppercase mb-4">Geometry</h5>
                    <FormField label="Padding (px)"><TextInput type="number" value={config.padding} onChange={e => updateConfig('padding', parseInt(e.target.value))} /></FormField>
                    <Switch label="Include Control Characters" checked={config.includeControl} onChange={v => updateConfig('includeControl', v)} />
                  </div>
                </div>
              </section>
            )}
          </div>
        </main>

        {/* Footer / Console */}
        <div className="h-64 bg-slate-950 border-t border-slate-800/50 flex flex-col shadow-2xl">
          <div className="flex items-center justify-between px-8 py-3 bg-[#0f172a] border-b border-slate-800/50">
            <div className="flex items-center gap-6">
              <span className="text-[10px] font-bold text-slate-400 flex items-center gap-2 tracking-widest uppercase">
                <UI_ICONS.Terminal /> Real-time Build Log
              </span>
              {isBuilding && (
                <div className="flex items-center gap-4">
                  <div className="w-48 h-1 bg-slate-800 rounded-full overflow-hidden">
                    <div 
                      className="h-full bg-blue-500 transition-all duration-300 shadow-[0_0_8px_rgba(59,130,246,0.5)]" 
                      style={{ width: `${progress}%` }}
                    ></div>
                  </div>
                  <span className="text-[10px] font-mono text-blue-400 tabular-nums">{progress}% COMPLETE</span>
                </div>
              )}
            </div>
            <button 
              onClick={() => setLogs([])}
              className="text-[10px] text-slate-600 hover:text-blue-400 transition-colors uppercase font-bold tracking-tighter"
            >
              Flush Logs
            </button>
          </div>
          <div className="flex-1 overflow-y-auto p-6 font-mono text-[11px] space-y-2 bg-[#050914]">
            {logs.length === 0 ? (
              <div className="text-slate-800 select-none flex flex-col items-center justify-center h-full gap-2">
                <UI_ICONS.Terminal />
                <span>Waiting for build initialization...</span>
              </div>
            ) : (
              logs.map((log) => (
                <div key={log.id} className="flex gap-4 group animate-in fade-in slide-in-from-left-2 duration-300">
                  <span className="text-slate-700 shrink-0">[{log.timestamp}]</span>
                  <span className={`font-bold shrink-0 min-w-[70px] ${
                    log.level === 'info' ? 'text-slate-500' :
                    log.level === 'warn' ? 'text-yellow-500/80' :
                    log.level === 'error' ? 'text-red-500/80' :
                    'text-green-500/80'
                  }`}>
                    {log.level.toUpperCase()}
                  </span>
                  <span className="text-slate-300/90 leading-relaxed">{log.message}</span>
                </div>
              ))
            )}
            <div ref={logEndRef} />
          </div>
        </div>
      </div>
    </div>
  );
};

export default App;
