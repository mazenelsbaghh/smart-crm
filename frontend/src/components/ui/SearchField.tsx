import type { InputHTMLAttributes, Ref } from 'react';
import { Search } from 'lucide-react';
import styles from './ui.module.css';

type SearchFieldProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'type' | 'aria-label'> & {
  label: string; inputRef?: Ref<HTMLInputElement>; containerClassName?: string;
};

export default function SearchField({ label, inputRef, containerClassName = '', ...props }: SearchFieldProps) {
  return <div className={`${styles.searchField} ${containerClassName}`}>
    <Search size={18} aria-hidden="true" />
    <input {...props} ref={inputRef} type="search" aria-label={label} />
  </div>;
}
