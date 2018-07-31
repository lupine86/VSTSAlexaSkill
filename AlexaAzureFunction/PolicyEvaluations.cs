using System;
using System.Collections.Generic;
using System.Text;

namespace AlexaVstsSkillAzureFunction
{
    class PolicyEvaluations
    {

        public class Rootobject
        {
            public Value[] value { get; set; }
            public int count { get; set; }
        }

        public class Value
        {
            public Configuration configuration { get; set; }
            public string artifactId { get; set; }
            public string evaluationId { get; set; }
            public DateTime startedDate { get; set; }
            public DateTime completedDate { get; set; }
            public string status { get; set; }
            public Context context { get; set; }
        }

        public class Configuration
        {
            public Createdby createdBy { get; set; }
            public DateTime createdDate { get; set; }
            public bool isEnabled { get; set; }
            public bool isBlocking { get; set; }
            public bool isDeleted { get; set; }
            public Settings settings { get; set; }
            public _Links1 _links { get; set; }
            public int revision { get; set; }
            public int id { get; set; }
            public string url { get; set; }
            public Type type { get; set; }
        }

        public class Createdby
        {
            public string displayName { get; set; }
            public string url { get; set; }
            public _Links _links { get; set; }
            public string id { get; set; }
            public string uniqueName { get; set; }
            public string imageUrl { get; set; }
            public string descriptor { get; set; }
        }

        public class _Links
        {
            public Avatar avatar { get; set; }
        }

        public class Avatar
        {
            public string href { get; set; }
        }

        public class Settings
        {
            public int buildDefinitionId { get; set; }
            public bool queueOnSourceUpdateOnly { get; set; }
            public bool manualQueueOnly { get; set; }
            public string displayName { get; set; }
            public float validDuration { get; set; }
            public Scope[] scope { get; set; }
            public string[] filenamePatterns { get; set; }
            public int minimumApproverCount { get; set; }
            public bool creatorVoteCounts { get; set; }
            public bool allowDownvotes { get; set; }
            public string[] requiredReviewerIds { get; set; }
            public bool addedFilesOnly { get; set; }
            public string statusName { get; set; }
            public string statusGenre { get; set; }
            public object authorId { get; set; }
            public bool invalidateOnSourceUpdate { get; set; }
            public string defaultDisplayName { get; set; }
            public int policyApplicability { get; set; }
        }

        public class Scope
        {
            public string refName { get; set; }
            public string matchKind { get; set; }
            public string repositoryId { get; set; }
        }

        public class _Links1
        {
            public Self self { get; set; }
            public Policytype policyType { get; set; }
        }

        public class Self
        {
            public string href { get; set; }
        }

        public class Policytype
        {
            public string href { get; set; }
        }

        public class Type
        {
            public string id { get; set; }
            public string url { get; set; }
            public string displayName { get; set; }
        }

        public class Context
        {
            public string lastMergeCommitId { get; set; }
            public int buildId { get; set; }
            public int buildDefinitionId { get; set; }
            public bool buildIsNotCurrent { get; set; }
            public DateTime buildStartedUtc { get; set; }
            public bool isExpired { get; set; }
            public bool buildAfterMerge { get; set; }
            public int? latestStatusId { get; set; }
        }

        public class Evaluation
        {
            public string EvaluationId { get; set; }
            public string ProjectId { get; set; }
        }
    }
}
