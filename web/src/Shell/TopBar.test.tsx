import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import TopBar from './TopBar';

describe('TopBar', () => {
  it('identifies the application as FO4IDE', () => {
    render(
      <TopBar
        activeTab="home"
        hasRecord={false}
        recordTitle=""
        onSelectTab={() => {}}
        onCloseRecord={() => {}}
        onOpenSettings={() => {}}
        onOpenHelp={() => {}}
        onToggleRail={() => {}}
        railVisible={false}
        onToggleChat={() => {}}
        chatVisible={false}
        isDark={true}
        onToggleTheme={() => {}}
        onSearch={async () => []}
        onOpenHit={() => {}}
      />,
    );

    const retiredBrand = 'Nexus' + 'Edit';

    expect(screen.getByText('FO4IDE')).toBeTruthy();
    expect(screen.queryByText(retiredBrand)).toBeNull();
  });
});
