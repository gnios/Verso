import type { ReactNode } from 'react';

/**
 * Link usado pelo LinkProvider do Astryx. Navegação real é resolvida pelos componentes
 * (onClick → bridge); aqui só impedimos o salto do href padrão dentro do shell desktop.
 */
export function NativeLink(props: { href: string; children?: ReactNode; className?: string }) {
  const { href, children, className } = props;
  return (
    <a
      href={href}
      className={className}
      onClick={(event) => {
        event.preventDefault();
      }}
    >
      {children}
    </a>
  );
}