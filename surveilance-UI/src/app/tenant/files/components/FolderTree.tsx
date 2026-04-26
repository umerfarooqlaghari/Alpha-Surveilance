'use client';

import { ChevronRight, ChevronDown, Folder, FolderOpen, Plus, Loader2 } from 'lucide-react';
import { useState } from 'react';
import type { FolderDto } from '@/lib/api/fileManager';

interface FolderTreeProps {
    folders: FolderDto[];
    activeFolderId: string | null;
    onFolderClick: (id: string) => void;
    onCreateFolder: (parentId: string | null) => void;
    loadChildren: (id: string) => Promise<FolderDto[]>;
}

interface TreeNode extends FolderDto {
    children?: TreeNode[];
    isOpen?: boolean;
    isLoading?: boolean;
}

export default function FolderTree({ folders, activeFolderId, onFolderClick, onCreateFolder, loadChildren }: FolderTreeProps) {
    const [tree, setTree] = useState<TreeNode[]>(folders.map(f => ({ ...f })));

    const toggleFolder = async (node: TreeNode) => {
        if (!node.isOpen && !node.children) {
            // Load children
            setTree(prev => updateNode(prev, node.id, { isLoading: true }));
            const children = await loadChildren(node.id);
            setTree(prev => updateNode(prev, node.id, { children: children.map(c => ({ ...c })), isLoading: false, isOpen: true }));
        } else {
            setTree(prev => updateNode(prev, node.id, { isOpen: !node.isOpen }));
        }
        onFolderClick(node.id);
    };

    const addToTree = (newFolder: FolderDto) => {
        if (!newFolder.parentFolderId) {
            setTree(prev => [...prev, { ...newFolder }]);
        } else {
            setTree(prev => updateNode(prev, newFolder.parentFolderId!, {
                children: undefined, // force reload next time
                childFolderCount: 1,
            }));
        }
    };

    return (
        <div className="py-2">
            <div className="flex items-center justify-between px-4 py-2">
                <span className="text-xs font-bold text-gray-400 uppercase tracking-widest">Folders</span>
                <button onClick={() => onCreateFolder(null)} title="New root folder"
                    className="text-gray-400 hover:text-blue-600 transition-colors">
                    <Plus className="w-3.5 h-3.5" />
                </button>
            </div>
            <TreeNodes nodes={tree} activeFolderId={activeFolderId} onToggle={toggleFolder} onCreateFolder={onCreateFolder} depth={0} />
        </div>
    );
}

function TreeNodes({ nodes, activeFolderId, onToggle, onCreateFolder, depth }: {
    nodes: TreeNode[];
    activeFolderId: string | null;
    onToggle: (n: TreeNode) => void;
    onCreateFolder: (id: string | null) => void;
    depth: number;
}) {
    return (
        <>
            {nodes.map(node => (
                <TreeNodeItem key={node.id} node={node} activeFolderId={activeFolderId}
                    onToggle={onToggle} onCreateFolder={onCreateFolder} depth={depth} />
            ))}
        </>
    );
}

function TreeNodeItem({ node, activeFolderId, onToggle, onCreateFolder, depth }: {
    node: TreeNode; activeFolderId: string | null;
    onToggle: (n: TreeNode) => void;
    onCreateFolder: (id: string | null) => void;
    depth: number;
}) {
    const isActive = node.id === activeFolderId;
    const hasChildren = node.childFolderCount > 0 || (node.children && node.children.length > 0);

    return (
        <div>
            <div
                className={`group flex items-center gap-1.5 px-3 py-2 cursor-pointer transition-colors rounded-lg mx-2 ${isActive ? 'bg-blue-50 text-blue-700' : 'text-gray-700 hover:bg-gray-50'}`}
                style={{ paddingLeft: `${12 + depth * 16}px` }}
                onClick={() => onToggle(node)}
            >
                {/* Expand arrow */}
                {hasChildren || node.isLoading ? (
                    node.isLoading ? <Loader2 className="w-3 h-3 animate-spin text-gray-400 flex-shrink-0" /> :
                        node.isOpen ? <ChevronDown className="w-3 h-3 flex-shrink-0" /> : <ChevronRight className="w-3 h-3 flex-shrink-0" />
                ) : (
                    <span className="w-3 h-3 flex-shrink-0" />
                )}
                {node.isOpen ? <FolderOpen className="w-4 h-4 flex-shrink-0 text-yellow-500" /> : <Folder className="w-4 h-4 flex-shrink-0 text-yellow-500" />}
                <span className="text-sm font-medium truncate flex-1">{node.name}</span>
                {/* Sub-folder create button appears on hover */}
                <button onClick={(e) => { e.stopPropagation(); onCreateFolder(node.id); }}
                    className="opacity-0 group-hover:opacity-100 text-gray-400 hover:text-blue-600 transition-all flex-shrink-0">
                    <Plus className="w-3 h-3" />
                </button>
            </div>
            {node.isOpen && node.children && (
                <TreeNodes nodes={node.children} activeFolderId={activeFolderId}
                    onToggle={onToggle} onCreateFolder={onCreateFolder} depth={depth + 1} />
            )}
        </div>
    );
}

function updateNode(nodes: TreeNode[], id: string, patch: Partial<TreeNode>): TreeNode[] {
    return nodes.map(n => {
        if (n.id === id) return { ...n, ...patch };
        if (n.children) return { ...n, children: updateNode(n.children, id, patch) };
        return n;
    });
}
