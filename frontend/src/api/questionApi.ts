import type {
  NoCtfapiEndpointsChallengesQuestionsAddCompetitionQuestionMessageRequest,
  NoCtfapiEndpointsChallengesQuestionsChangeCompetitionQuestionStatusRequest,
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionFailureResponse,
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionResponse,
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionStatusCode,
  NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionSubjectCode,
  NoCtfapiEndpointsChallengesQuestionsCreateCompetitionQuestionRequest,
  NoCtfapiEndpointsChallengesQuestionsPublishCompetitionQuestionRequest,
} from './generated/types.gen'
import { translate as tt } from '@/i18n'
import * as generatedSdk from './generated/sdk.gen'
import { ApiError } from './noctf'

interface GeneratedResult<T> {
  data?: T
  error?: unknown
  response?: Response
}

function unwrap<T>(result: GeneratedResult<T>): T {
  if (result.error) {
    throw new ApiError(
      tt('errors.requestFailed'),
      result.response?.status,
      result.error,
    )
  }
  return result.data as T
}

export type CompetitionQuestion
  = NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionResponse
export type CompetitionQuestionSubject
  = NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionSubjectCode
export type CompetitionQuestionStatus
  = NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionStatusCode
export type CompetitionQuestionConflict
  = NoCtfapiEndpointsChallengesQuestionsCompetitionQuestionFailureResponse

export const questionApi = {
  async list(
    competitionId: string,
    options: {
      competitionChallengeId?: string | null
      subject?: CompetitionQuestionSubject | null
      status?: CompetitionQuestionStatus | null
      publishedOnly?: boolean
      limit?: number
    } = {},
  ): Promise<CompetitionQuestion[]> {
    const response = unwrap(await generatedSdk.listCompetitionQuestions({
      path: { competitionId },
      query: {
        competitionChallengeId: options.competitionChallengeId,
        subject: options.subject,
        status: options.status,
        publishedOnly: options.publishedOnly ?? false,
        limit: options.limit ?? 50,
      },
    }))
    return response.items ?? []
  },

  async get(competitionId: string, questionId: string): Promise<CompetitionQuestion> {
    return unwrap(await generatedSdk.getCompetitionQuestion({
      path: { competitionId, questionId },
    }))
  },

  async create(
    competitionId: string,
    body: NoCtfapiEndpointsChallengesQuestionsCreateCompetitionQuestionRequest,
  ): Promise<CompetitionQuestion> {
    return unwrap(await generatedSdk.createCompetitionQuestion({
      path: { competitionId },
      body,
    }))
  },

  async addMessage(
    competitionId: string,
    questionId: string,
    body: NoCtfapiEndpointsChallengesQuestionsAddCompetitionQuestionMessageRequest,
  ): Promise<CompetitionQuestion> {
    return unwrap(await generatedSdk.addCompetitionQuestionMessage({
      path: { competitionId, questionId },
      body,
    }))
  },

  async changeStatus(
    competitionId: string,
    questionId: string,
    body: NoCtfapiEndpointsChallengesQuestionsChangeCompetitionQuestionStatusRequest,
  ): Promise<CompetitionQuestion> {
    return unwrap(await generatedSdk.changeCompetitionQuestionStatus({
      path: { competitionId, questionId },
      body,
    }))
  },

  async publish(
    competitionId: string,
    questionId: string,
    body: NoCtfapiEndpointsChallengesQuestionsPublishCompetitionQuestionRequest,
  ): Promise<CompetitionQuestion> {
    return unwrap(await generatedSdk.publishCompetitionQuestion({
      path: { competitionId, questionId },
      body,
    }))
  },
}
