import type { OpenAiImageModel } from './types';

export default function ImageModelSelect({ label, provider, model = 'gpt-image-2.5-sunburst', disabled, className, onChange }: {
  label: string;
  provider: 'Gemini' | 'OpenAI';
  model?: OpenAiImageModel;
  disabled: boolean;
  className: string;
  onChange: (selection: { imageProvider: 'Gemini' | 'OpenAI'; openAiImageModel: OpenAiImageModel }) => void;
}) {
  return <label className={className}><span>{label}</span>
    <select value={provider === 'Gemini' ? 'Gemini' : model} disabled={disabled} onChange={event => {
      const selected = event.target.value;
      onChange({ imageProvider: selected === 'Gemini' ? 'Gemini' : 'OpenAI',
        openAiImageModel: selected === 'Gemini' ? model : selected as OpenAiImageModel });
    }}>
      <option value="Gemini">Gemini: صور 4K</option>
      <option value="gpt-image-2.5-sunburst">ChatGPT: Sunburst — أدق في التعديل</option>
      <option value="gpt-image-2.5-flare">ChatGPT: Flare — النسخة السريعة</option>
    </select>
    <small>{provider === 'Gemini' ? 'Gemini 3 Pro Image.' : model === 'gpt-image-2.5-flare' ? 'GPT Image 2.5 Flare للتوليد السريع.' : 'GPT Image 2.5 Sunburst للدقة في التصميم والتعديل.'}</small>
  </label>;
}
