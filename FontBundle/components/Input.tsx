
import React, { useState, useRef } from 'react';

interface FormFieldProps {
  label: string;
  description?: string;
  children: React.ReactNode;
  required?: boolean;
}

export const FormField: React.FC<FormFieldProps> = ({ label, description, children, required }) => (
  <div className="mb-6">
    <div className="flex justify-between items-baseline gap-4 mb-2">
      <div className="flex items-center gap-3">
        <label className="text-sm font-semibold text-slate-200">{label}</label>
        {required && <span className="text-[10px] bg-blue-500/20 text-blue-400 px-1.5 py-0.5 rounded uppercase border border-blue-500/30">Required</span>}
      </div>
      {description && <span className="text-xs text-slate-500 ml-2">{description}</span>}
    </div>
    {children}
  </div>
);

interface FileDropZoneProps {
  onFileSelect: (path: string) => void;
  value: string;
  placeholder: string;
  accept?: string;
  icon: React.ReactNode;
}

export const FileDropZone: React.FC<FileDropZoneProps> = ({ onFileSelect, value, placeholder, accept, icon }) => {
  const [isDragging, setIsDragging] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(true);
  };

  const handleDragLeave = () => {
    setIsDragging(false);
  };

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(false);
    if (e.dataTransfer.files && e.dataTransfer.files[0]) {
      // In a real app we'd get the path, here we simulate it
      onFileSelect(`C:/Desktop/Assets/${e.dataTransfer.files[0].name}`);
    }
  };

  const handleClick = () => {
    fileInputRef.current?.click();
  };

  const handleInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files[0]) {
      onFileSelect(`C:/Project/Imports/${e.target.files[0].name}`);
    }
  };

  return (
    <div
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      onDrop={handleDrop}
      onClick={handleClick}
      className={`relative group cursor-pointer border-2 border-dashed rounded-xl p-8 transition-all duration-300 flex flex-col items-center justify-center gap-4 ${
        value 
          ? 'bg-blue-500/5 border-blue-500/50' 
          : isDragging 
            ? 'bg-blue-600/10 border-blue-400 scale-[1.01]' 
            : 'bg-slate-900/50 border-slate-800 hover:border-slate-600 hover:bg-slate-900'
      }`}
    >
      <input 
        type="file" 
        ref={fileInputRef} 
        onChange={handleInputChange} 
        className="hidden" 
        accept={accept}
      />
      
      <div className={`p-4 rounded-full transition-transform duration-300 group-hover:scale-110 ${
        value ? 'bg-blue-500 text-white shadow-lg shadow-blue-500/20' : 'bg-slate-800 text-slate-400'
      }`}>
        {icon}
      </div>

      <div className="text-center">
        <p className={`text-sm font-medium ${value ? 'text-blue-400' : 'text-slate-300'}`}>
          {value ? value.split('/').pop() : placeholder}
        </p>
        <p className="text-xs text-slate-500 mt-1">
          {value ? 'Click or drag to replace' : 'Click to browse or drop file here'}
        </p>
      </div>

      {value && (
        <div className="absolute top-3 right-3">
          <svg className="w-5 h-5 text-blue-500" fill="currentColor" viewBox="0 0 20 20">
            <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clipRule="evenodd" />
          </svg>
        </div>
      )}
    </div>
  );
};

interface TextInputProps extends React.InputHTMLAttributes<HTMLInputElement> {
  icon?: React.ReactNode;
}

export const TextInput: React.FC<TextInputProps> = ({ icon, className, ...props }) => (
  <div className="relative">
    {icon && (
      <div className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-500">
        {icon}
      </div>
    )}
    <input
      {...props}
      className={`w-full bg-slate-900 border border-slate-800 rounded-lg py-3 ${
        icon ? 'pl-10' : 'px-4'
      } pr-4 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent transition-all placeholder:text-slate-600 ${className}`}
    />
  </div>
);

export const Switch: React.FC<{
  checked: boolean;
  onChange: (val: boolean) => void;
  label: string;
}> = ({ checked, onChange, label }) => (
  <label className="flex items-center cursor-pointer group mb-3">
    <div className="relative">
      <input
        type="checkbox"
        className="sr-only"
        checked={checked}
        onChange={(e) => onChange(e.target.checked)}
      />
      <div className={`block w-10 h-6 rounded-full transition-colors ${checked ? 'bg-blue-600' : 'bg-slate-700'}`}></div>
      <div className={`absolute left-1 top-1 bg-white w-4 h-4 rounded-full transition-transform ${checked ? 'translate-x-4' : 'translate-x-0'}`}></div>
    </div>
    <span className="ml-3 text-sm text-slate-300 group-hover:text-white transition-colors">
      {label}
    </span>
  </label>
);

export const Select: React.FC<React.SelectHTMLAttributes<HTMLSelectElement>> = ({ children, className, ...props }) => (
  <select
    {...props}
    className={`w-full bg-slate-900 border border-slate-800 rounded-lg py-3 px-4 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent transition-all cursor-pointer ${className}`}
  >
    {children}
  </select>
);
