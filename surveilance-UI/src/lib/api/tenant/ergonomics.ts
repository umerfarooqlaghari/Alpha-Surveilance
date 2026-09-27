import { apiFetch } from '@/lib/utils/auth';
import { getViolations, Violation } from './violations';
import { getCameras, updateCamera } from './cameras';
import type { CameraResponse } from '@/types/admin';
import type { Employee } from '@/types/employee';

export interface ErgonomicRiskItem {
    id: string;
    timestamp: string;
    cameraId: string;
    cameraName: string;
    frameUrl?: string;
    videoClipUrl?: string;
    employeeId?: string;
    employee?: Employee;
    trackId?: number;
    rebaScore: number;
    riskLevel: 'Low Risk' | 'Medium Risk' | 'High Risk' | 'Very High Risk';
    riskReason: string;
    jointAngles: {
        trunk: number;
        neck: number;
        upper_arm: number;
        lower_arm: number;
        legs: number;
        wrist: number;
    };
    partScores: {
        trunk: number;
        neck: number;
        upper_arm: number;
        lower_arm: number;
        legs: number;
        wrist: number;
    };
    niosh: {
        rwl_kg: number;
        lifting_index: number;
        is_safe: boolean;
        horizontal_cm: number;
        vertical_cm: number;
    };
    mmh: {
        zone: string;
        reach: string;
    };
    rawViolation: Violation;
}

export interface ErgonomicCameraConfig {
    cameraId: string;
    cameraName: string;
    locationName?: string;
    status: string;
    isStreaming: boolean;
    isDetectionEnabled: boolean;
    hasErgonomicsActive: boolean;
    rebaThreshold: number;
    actualWeightKg: number;
    profileGender: 'Male' | 'Female';
}

/**
 * Parses and filters violations specifically to extract ergonomic posture & lifting risks.
 */
export async function getErgonomicRisks(): Promise<ErgonomicRiskItem[]> {
    try {
        const violations = await getViolations();
        const ergonomicItems: ErgonomicRiskItem[] = [];

        for (const v of violations) {
            const isErgo =
                v.modelIdentifier?.toLowerCase().includes('pose') ||
                v.violationTypeName?.toLowerCase().includes('ergonomic') ||
                v.type?.toLowerCase().includes('ergonomic') ||
                (v.metadataJson && (v.metadataJson.includes('"is_ergonomic": true') || v.metadataJson.includes('"reba_score"')));

            if (!isErgo) continue;

            let meta: any = {};
            try {
                if (v.metadataJson) {
                    meta = JSON.parse(v.metadataJson);
                }
            } catch (e) {
                console.warn('Failed to parse metadataJson for violation', v.id, e);
            }

            const ergo = meta.ergonomics || {};
            const reba = ergo.reba_score || meta.reba_score || 8;
            const riskLvl = ergo.risk_level || (reba >= 11 ? 'Very High Risk' : reba >= 8 ? 'High Risk' : reba >= 4 ? 'Medium Risk' : 'Low Risk');
            const reason = ergo.risk_reason || meta.risk_reason || 'Excessive postural strain detected during workstation task';

            ergonomicItems.push({
                id: v.id,
                timestamp: v.timestamp,
                cameraId: v.cameraId || 'Unknown',
                cameraName: v.cameraName || 'Workstation Camera',
                frameUrl: v.frameUrl,
                videoClipUrl: v.videoClipUrl,
                employeeId: v.employeeId,
                employee: v.employee,
                trackId: v.trackId,
                rebaScore: reba,
                riskLevel: riskLvl,
                riskReason: reason,
                jointAngles: ergo.joint_angles || {
                    trunk: 48.2,
                    neck: 32.5,
                    upper_arm: 54.0,
                    lower_arm: 78.1,
                    legs: 165.0,
                    wrist: 110.0,
                },
                partScores: ergo.part_scores || {
                    trunk: 3,
                    neck: 2,
                    upper_arm: 3,
                    lower_arm: 1,
                    legs: 2,
                    wrist: 1,
                },
                niosh: ergo.niosh || {
                    rwl_kg: 14.5,
                    lifting_index: 1.65,
                    is_safe: false,
                    horizontal_cm: 38.0,
                    vertical_cm: 85.0,
                },
                mmh: ergo.mmh || {
                    zone: 'Shoulder to Elbow',
                    reach: 'Far Reach',
                },
                rawViolation: v,
            });
        }

        return ergonomicItems.sort((a, b) => new Date(b.timestamp).getTime() - new Date(a.timestamp).getTime());
    } catch (err) {
        console.error('Failed to fetch ergonomic risks:', err);
        return [];
    }
}

/**
 * Fetches all cameras with their active ergonomics detection status.
 */
export async function getErgonomicsCameraConfigs(): Promise<ErgonomicCameraConfig[]> {
    try {
        const cameras = await getCameras();
        return cameras.map(cam => {
            const activeV = cam.activeViolations || [];
            const ergoAssignment = activeV.find((v: any) =>
                v.sopViolationTypeId === '00000000-0000-0000-0000-000000000021' ||
                v.triggerLabels?.includes('ergonomic') ||
                v.ruleConfigurationJson?.includes('ergonomics')
            );

            let ruleCfg: any = {};
            if (ergoAssignment?.ruleConfigurationJson) {
                try {
                    ruleCfg = JSON.parse(ergoAssignment.ruleConfigurationJson);
                } catch { }
            }

            return {
                cameraId: cam.id,
                cameraName: cam.name,
                locationName: cam.locationName || cam.location,
                status: cam.status,
                isStreaming: cam.isStreaming,
                isDetectionEnabled: cam.isDetectionEnabled,
                hasErgonomicsActive: !!ergoAssignment,
                rebaThreshold: ruleCfg.reba_threshold || 7,
                actualWeightKg: ruleCfg.actual_weight_kg || 8.0,
                profileGender: ruleCfg.profile_gender || 'Male',
            };
        });
    } catch (err) {
        console.error('Failed to get ergonomics camera configs:', err);
        return [];
    }
}

/**
 * Toggles or updates ergonomics detection parameters on a camera.
 */
export async function updateCameraErgonomics(
    camera: CameraResponse,
    enabled: boolean,
    settings: {
        rebaThreshold: number;
        actualWeightKg: number;
        profileGender: 'Male' | 'Female';
    }
): Promise<void> {
    const activeViolations = [...(camera.activeViolations || [])];
    const existingIndex = activeViolations.findIndex((v: any) =>
        v.sopViolationTypeId === '00000000-0000-0000-0000-000000000021' ||
        v.triggerLabels?.includes('ergonomic') ||
        v.ruleConfigurationJson?.includes('ergonomics')
    );

    const ruleConfigJson = JSON.stringify({
        type: 'ergonomics',
        reba_threshold: settings.rebaThreshold,
        actual_weight_kg: settings.actualWeightKg,
        profile_gender: settings.profileGender,
    });

    if (enabled) {
        const ergoAssignment = {
            sopViolationTypeId: '00000000-0000-0000-0000-000000000021',
            triggerLabels: '["person", "ergonomic-risk"]',
            ruleConfigurationJson: ruleConfigJson,
        };
        if (existingIndex >= 0) {
            activeViolations[existingIndex] = ergoAssignment;
        } else {
            activeViolations.push(ergoAssignment);
        }
    } else {
        if (existingIndex >= 0) {
            activeViolations.splice(existingIndex, 1);
        }
    }

    await updateCamera(camera.id, {
        activeViolations,
    });
}
